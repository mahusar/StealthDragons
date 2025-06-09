using UnityEngine;
using UnityEngine.UI;
using Mirror;
using UnityEngine.SceneManagement;

public class LeaveRoomUI : MonoBehaviour
{
    public Button leaveButton; // Assign in Inspector for RoomOnline and DragonMatch scenes
    private XSTDragonNetworkManager manager;
    private RoomListUI roomListUI; // Reference to RoomListUI for refreshing

    void Awake()
    {
        if (NetworkManager.singleton == null)
        {
            Debug.LogError("NetworkManager.singleton is null in LeaveRoomUI.");
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

        // Find RoomListUI in the offline scene (optional, if already loaded)
        roomListUI = FindObjectOfType<RoomListUI>();
    }

    void Start()
    {
        if (leaveButton != null)
        {
            leaveButton.onClick.AddListener(LeaveRoom);
        }
        else
        {
            Debug.LogWarning("Leave Button not assigned in LeaveRoomUI.");
        }
    }

    public void LeaveRoom()
    {
        if (manager == null)
        {
            Debug.LogError("XSTDragonNetworkManager is null in LeaveRoom.");
            return;
        }

        // Notify matchmaker based on role
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            // Host: Stop the server and notify matchmaker to remove the room
            manager.SendToMatchmaker($"REMOVE_ROOM|{manager.networkAddress}|{manager.networkPort}");
            manager.StopHost();
            Debug.Log("Host stopped server and left the room.");
        }
        else if (NetworkClient.isConnected)
        {
            // Client: Disconnect and notify matchmaker of player leaving
            manager.SendToMatchmaker($"PLAYER_LEAVE|{manager.networkAddress}|{manager.networkPort}");
            manager.StopClient();
            Debug.Log("Client disconnected from the server.");
        }
        else
        {
            Debug.LogWarning("Not connected to any server.");
        }

        // Load offline scene and refresh room list
        SceneManager.sceneLoaded += OnOfflineSceneLoaded;
        SceneManager.LoadScene("RoomOffline");
    }

    private void OnOfflineSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "RoomOffline")
        {
            // Find RoomListUI in the offline scene
            roomListUI = FindObjectOfType<RoomListUI>();
            if (roomListUI != null)
            {
                roomListUI.RefreshRoomList();
                Debug.Log("Room list refreshed after returning to offline scene.");
            }
            else
            {
                Debug.LogWarning("RoomListUI not found in offline scene.");
            }
        }
        // Unsubscribe to avoid multiple calls
        SceneManager.sceneLoaded -= OnOfflineSceneLoaded;
    }
}