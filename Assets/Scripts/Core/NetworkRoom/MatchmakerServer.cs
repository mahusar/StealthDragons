using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class MatchmakerServer : MonoBehaviour
{
    [Serializable]
    public class RoomInfo
    {
        public string roomName;
        public string onionAddress;
        public int port;
        public int playerCount = 0;
        public float lastActiveTime;

        public RoomInfo(string name, string onion, int port)
        {
            roomName = name;
            onionAddress = onion;
            this.port = port;
            lastActiveTime = Time.time;
        }

        public string GetStatus() => playerCount < 2 ? "Open" : "Full";
    }

    private List<RoomInfo> rooms = new List<RoomInfo>();
    private TcpListener tcpServer;
    private bool isRunning;
    public int matchmakerPort = 5555;

    private const string GameVersion = "0.4";   // GAME VERSION

    void Start()
    {
        try
        {
            tcpServer = new TcpListener(IPAddress.Loopback, matchmakerPort);
            tcpServer.Start();
            isRunning = true;
            StartCoroutine(AcceptClients());
            StartCoroutine(CleanupInactiveRooms());
            Debug.Log($"[Matchmaker] TCP server started on port {matchmakerPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Matchmaker] Failed to start: {e.Message}");
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        tcpServer?.Stop();
        Debug.Log("[Matchmaker] Stopped");
    }

    IEnumerator AcceptClients()
    {
        while (isRunning)
        {
            if (tcpServer.Pending())
            {
                TcpClient client = tcpServer.AcceptTcpClient();
                StartCoroutine(HandleClient(client));
            }
            yield return null;
        }
    }

    IEnumerator HandleClient(TcpClient client)
    {
        yield return null;
        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
            {
                string message = reader.ReadLine();
                if (string.IsNullOrEmpty(message)) yield break;

                Debug.Log($"[Matchmaker] Received: {message}");
                ProcessMessage(message, writer);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Matchmaker] Client error: {e.Message}");
        }
    }

    void ProcessMessage(string message, StreamWriter writer)
    {
        if (message.StartsWith("REGISTER"))
        {
            // REGISTER|roomName|onionAddress|port
            string[] data = message.Split('|');
            if (data.Length < 4) return;

            string roomName = data[1];
            string onion = data[2];
            if (!int.TryParse(data[3], out int port)) return;

            var existing = rooms.Find(r => r.onionAddress == onion && r.port == port);
            if (existing == null)
            {
                rooms.Add(new RoomInfo(roomName, onion, port));
                Debug.Log($"[Matchmaker] Registered room: {roomName} at {onion}:{port}");
            }
            else
            {
                existing.roomName = roomName;
                existing.lastActiveTime = Time.time;
            }
            writer.WriteLine("OK");
        }
        else if (message == "GET_ROOMS")
        {
            string roomList = string.Join(",", rooms.ConvertAll(r =>
                $"{r.roomName}|{r.onionAddress}|{r.port}|{r.GetStatus()}|{r.playerCount}"));
            writer.WriteLine(string.IsNullOrEmpty(roomList) ? "EMPTY" : roomList);
            Debug.Log($"[Matchmaker] Sent room list: {roomList}");
        }
        else if (message.StartsWith("PING"))
        {
            // PING|onionAddress|port
            string[] data = message.Split('|');
            if (data.Length < 3 || !int.TryParse(data[2], out int port)) return;
            var room = rooms.Find(r => r.onionAddress == data[1] && r.port == port);
            if (room != null) room.lastActiveTime = Time.time;
            writer.WriteLine("OK");
        }
        else if (message.StartsWith("PLAYER_JOIN"))
        {
            string[] data = message.Split('|');
            if (data.Length < 3 || !int.TryParse(data[2], out int port)) return;
            var room = rooms.Find(r => r.onionAddress == data[1] && r.port == port);
            if (room != null && room.playerCount < 2)
            {
                room.playerCount++;
                room.lastActiveTime = Time.time;
            }
            writer.WriteLine("OK");
        }
        else if (message.StartsWith("PLAYER_LEAVE"))
        {
            string[] data = message.Split('|');
            if (data.Length < 3 || !int.TryParse(data[2], out int port)) return;
            var room = rooms.Find(r => r.onionAddress == data[1] && r.port == port);
            if (room != null && room.playerCount > 0)
            {
                room.playerCount--;
                room.lastActiveTime = Time.time;
            }
            writer.WriteLine("OK");
        }
        else if (message.StartsWith("DEREGISTER"))
        {
            string[] data = message.Split('|');
            if (data.Length < 3 || !int.TryParse(data[2], out int port)) return;
            rooms.RemoveAll(r => r.onionAddress == data[1] && r.port == port);
            writer.WriteLine("OK");
        }
        else if (message == "GET_STATUS")
        {
            string path = Application.persistentDataPath + "/status.txt";
            string count = File.Exists(path) ? File.ReadAllText(path).Trim() : "0";
            writer.WriteLine(count);
        }
        else if (message == "GET_VERSION")
        {
            writer.WriteLine(GameVersion);
        }
    }

    IEnumerator CleanupInactiveRooms()
    {
        while (isRunning)
        {
            yield return new WaitForSeconds(30f);
            int removed = rooms.RemoveAll(r => Time.time - r.lastActiveTime > 60f);
            if (removed > 0)
                Debug.Log($"[Matchmaker] Cleaned {removed} inactive rooms. Remaining: {rooms.Count}");
        }
    }
}