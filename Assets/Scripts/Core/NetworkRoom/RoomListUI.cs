using Mirror;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomListUI : MonoBehaviour
{
    public Button refreshButton;
    public GameObject roomPrefab;
    public Transform roomListParent;
    public TMP_Text roomListText;
    public string matchmakerAddress = "127.0.0.1";
    public int matchmakerPort = 5555;

    private XSTDragonNetworkManager manager;

    void Awake()
    {
        if (NetworkManager.singleton == null)
        {
            Debug.LogError("NetworkManager.singleton is null in RoomListUI.");
            gameObject.SetActive(false);
            return;
        }

        manager = NetworkManager.singleton as XSTDragonNetworkManager;
        if (manager == null)
        {
            Debug.LogError("XSTDragonNetworkManager not found.");
            gameObject.SetActive(false);
            return;
        }
    }

    void Start()
    {
        if (refreshButton != null)
        {
            refreshButton.onClick.AddListener(RefreshRoomList);
        }
        else
        {
            Debug.LogWarning("Refresh Button not assigned.");
        }
    }

    public void RefreshRoomList()
    {
        // Clear existing room UI
        foreach (Transform child in roomListParent)
        {
            Destroy(child.gameObject);
        }

        try
        {
            using (var client = new UdpClient())
            {
                byte[] data = Encoding.UTF8.GetBytes("GET_ROOMS");
                client.Send(data, data.Length, matchmakerAddress, matchmakerPort);

                client.Client.ReceiveTimeout = 2000;
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] receivedData = client.Receive(ref remoteEndPoint);
                string receivedText = Encoding.UTF8.GetString(receivedData);

                if (string.IsNullOrEmpty(receivedText))
                {
                    roomListText.text = "No rooms available.";
                    Debug.Log("No rooms found.");
                    return;
                }

                string[] rooms = receivedText.Split(',');
                string displayText = "Available Rooms:\n";
                foreach (string room in rooms)
                {
                    string[] roomData = room.Split('|');
                    if (roomData.Length >= 5)
                    {
                        string roomName = roomData[0];
                        string ip = roomData[1];
                        string port = roomData[2];
                        string status = roomData[3];
                        string playerCount = roomData[4];

                        displayText += $"{roomName} ({playerCount}/2 players) - {status}\n";

                        // Create room UI
                        GameObject roomObj = Instantiate(roomPrefab, roomListParent);
                        TMP_Text[] texts = roomObj.GetComponentsInChildren<TMP_Text>(true);
                        if (texts.Length >= 4)
                        {
                            texts[0].text = roomName;
                            texts[1].text = $"{ip}:{port}";
                            texts[2].text = status;
                            texts[3].text = $"{playerCount}/2";
                        }
                        else
                        {
                            Debug.LogWarning("roomPrefab missing required TMP_Text components (need 4).");
                        }

                        Button joinButton = roomObj.GetComponentInChildren<Button>(true);
                        if (joinButton != null)
                        {
                            joinButton.interactable = int.Parse(playerCount) < 2;
                            joinButton.onClick.RemoveAllListeners();
                            joinButton.onClick.AddListener(() => JoinRoom(ip, int.Parse(port)));
                        }
                        else
                        {
                            Debug.LogWarning("roomPrefab missing Join Button.");
                        }
                    }
                }
                roomListText.text = displayText;
                Debug.Log($"Received room list: {receivedText}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to get room list: {e.Message}");
            roomListText.text = "Error fetching rooms.";
        }
    }

    void JoinRoom(string ip, int port)
    {
        if (manager == null)
        {
            Debug.LogError("XSTDragonNetworkManager is null in JoinRoom.");
            return;
        }

        Debug.Log($"Attempting to join room at {ip}:{port}");

        manager.networkAddress = ip;
        manager.networkPort = port;
        manager.UpdateTransportPort();

        if (NetworkClient.active)
        {
            Debug.Log("Stopping active client before joining new room.");
            manager.StopClient();
        }

        try
        {
            manager.StartClient();
            Debug.Log($"Started client for {ip}:{port}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to start client: {e.Message}");
        }
    }
}