using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class MatchmakerServer : MonoBehaviour
{
    [System.Serializable]
    public class RoomInfo
    {
        public string roomName;
        public string ip;
        public int port;
        public int playerCount = 0; // Start at 0
        public float lastActiveTime;

        public RoomInfo(string name, string ip, int port)
        {
            roomName = name;
            this.ip = ip;
            this.port = port;
            lastActiveTime = Time.time;
        }

        public string GetStatus() => playerCount < 2 ? "Open" : "Full";
    }

    private List<RoomInfo> rooms = new List<RoomInfo>();
    private UdpClient udpServer;
    private bool isRunning;
    public string matchmakerIp = "0.0.0.0";
    public int matchmakerPort = 5555;

    void Start()
    {
        try
        {
            udpServer = new UdpClient(new IPEndPoint(IPAddress.Parse(matchmakerIp), matchmakerPort));
            isRunning = true;
            StartCoroutine(CleanupInactiveRooms());
            StartCoroutine(ListenForData());
            Debug.Log($"Matchmaker server started on {matchmakerIp}:{matchmakerPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start matchmaker server: {e.Message}");
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        udpServer?.Close();
        udpServer = null;
        Debug.Log("Matchmaker server stopped");
    }

    IEnumerator ListenForData()
    {
        while (isRunning)
        {
            try
            {
                if (udpServer != null && udpServer.Available > 0)
                {
                    IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedData = udpServer.Receive(ref clientEndPoint);
                    string receivedText = Encoding.UTF8.GetString(receivedData);
                    Debug.Log($"Received from {clientEndPoint}: {receivedText}");

                    ProcessMessage(receivedText, clientEndPoint);
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.WouldBlock)
                {
                    Debug.LogWarning($"Socket error: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error receiving data: {e.Message}");
            }
            yield return null;
        }
    }

    void ProcessMessage(string receivedText, IPEndPoint clientEndPoint)
    {
        try
        {
            if (receivedText.StartsWith("REGISTER"))
            {
                string[] data = receivedText.Split('|');
                if (data.Length < 4) { Debug.LogWarning($"Invalid REGISTER: {receivedText}"); return; }
                string roomName = data[1];
                string ip = data[2];
                if (!int.TryParse(data[3], out int port)) { Debug.LogWarning($"Invalid port: {receivedText}"); return; }

                RoomInfo existingRoom = rooms.Find(r => r.ip == ip && r.port == port);
                if (existingRoom == null)
                {
                    rooms.Add(new RoomInfo(roomName, ip, port));
                    Debug.Log($"Registered {roomName} at {ip}:{port}, Players: 0");
                }
                else
                {
                    existingRoom.roomName = roomName;
                    existingRoom.lastActiveTime = Time.time;
                    Debug.Log($"Updated {roomName} at {ip}:{port}");
                }
            }
            else if (receivedText == "GET_ROOMS")
            {
                string roomList = string.Join(",", rooms.ConvertAll(r => $"{r.roomName}|{r.ip}|{r.port}|{r.GetStatus()}|{r.playerCount}"));
                byte[] sendData = Encoding.UTF8.GetBytes(roomList);
                udpServer.Send(sendData, sendData.Length, clientEndPoint);
                Debug.Log($"Sent to {clientEndPoint}: {roomList}");
            }
            else if (receivedText.StartsWith("PING"))
            {
                string[] data = receivedText.Split('|');
                if (data.Length < 3 || !int.TryParse(data[2], out int port)) { Debug.LogWarning($"Invalid PING: {receivedText}"); return; }
                string ip = data[1];

                RoomInfo room = rooms.Find(r => r.ip == ip && r.port == port);
                if (room != null)
                {
                    room.lastActiveTime = Time.time;
                    Debug.Log($"Ping for {ip}:{port}, Players: {room.playerCount}");
                }
            }
            else if (receivedText.StartsWith("DEREGISTER"))
            {
                string[] data = receivedText.Split('|');
                if (data.Length < 3 || !int.TryParse(data[3], out int port)) { Debug.LogWarning($"Invalid DEREGISTER: {receivedText}"); return; }
                string ip = data[1];

                rooms.RemoveAll(r => r.ip == ip && r.port == port);
                Debug.Log($"Deregistered {ip}:{port}. Rooms left: {rooms.Count}");
            }
            else if (receivedText.StartsWith("PLAYER_JOIN"))
            {
                string[] data = receivedText.Split('|');
                if (data.Length < 3 || !int.TryParse(data[2], out int port)) { Debug.LogWarning($"Invalid PLAYER_JOIN: {receivedText}"); return; }
                string ip = data[1];

                RoomInfo room = rooms.Find(r => r.ip == ip && r.port == port);
                if (room != null && room.playerCount < 2)
                {
                    room.playerCount++;
                    room.lastActiveTime = Time.time;
                    Debug.Log($"Player joined {ip}:{port}. Players: {room.playerCount}");
                }
            }
            else if (receivedText.StartsWith("PLAYER_LEAVE"))
            {
                string[] data = receivedText.Split('|');
                if (data.Length < 3 || !int.TryParse(data[2], out int port)) { Debug.LogWarning($"Invalid PLAYER_LEAVE: {receivedText}"); return; }
                string ip = data[1];

                RoomInfo room = rooms.Find(r => r.ip == ip && r.port == port);
                if (room != null && room.playerCount > 0)
                {
                    room.playerCount--;
                    room.lastActiveTime = Time.time;
                    Debug.Log($"Player left {ip}:{port}. Players: {room.playerCount}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Error processing message: {e.Message}");
        }
    }

    IEnumerator CleanupInactiveRooms()
    {
        float cleanupInterval = 30f;
        float roomTimeout = 60f;

        while (isRunning)
        {
            yield return new WaitForSeconds(cleanupInterval);
            float currentTime = Time.time;
            int removed = rooms.RemoveAll(room => currentTime - room.lastActiveTime > roomTimeout);
            if (removed > 0)
            {
                Debug.Log($"Cleaned up {removed} inactive rooms. Remaining: {rooms.Count}");
            }
        }
    }
}