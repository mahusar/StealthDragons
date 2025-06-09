using Mirror;
using System.Collections;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CreateRoomUI : MonoBehaviour
{
    public TMP_InputField roomNameInput;
    public Button createButton;
    public string matchmakerAddress = "127.0.0.1";
    public int matchmakerPort = 5555;

    private XSTDragonNetworkManager manager;

    void Awake()
    {
        if (NetworkManager.singleton == null)
        {
            Debug.LogError("NetworkManager.singleton is null in CreateRoomUI.");
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
        if (createButton != null)
        {
            createButton.onClick.AddListener(() => StartCoroutine(CreateRoomCoroutine()));
        }
        else
        {
            Debug.LogWarning("Create Button not assigned.");
        }
    }

    private IEnumerator CreateRoomCoroutine()
    {
        if (manager == null)
        {
            Debug.LogError("XSTDragonNetworkManager is null.");
            yield break;
        }

        string roomName = roomNameInput != null && !string.IsNullOrEmpty(roomNameInput.text)
            ? roomNameInput.text
            : $"Room_{Random.Range(1, 10)}";

        // Register room with VPS IP
        string message = $"REGISTER|{roomName}|{matchmakerAddress}|7777";
        bool registered = false;
        try
        {
            using (var client = new UdpClient())
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                client.Send(data, data.Length, matchmakerAddress, matchmakerPort);
                Debug.Log($"Sent to matchmaker: {message}");
                registered = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to register room: {e.Message}");
            yield break;
        }

        if (!registered)
        {
            Debug.LogError("Room registration failed.");
            yield break;
        }

        // Connect to VPS server
        manager.networkAddress = matchmakerAddress;
        manager.networkPort = 7777;
        manager.UpdateTransportPort();

        if (NetworkClient.active)
        {
            Debug.Log("Stopping active client before connecting.");
            manager.StopClient();
            yield return new WaitForSeconds(1f); // Wait for cleanup
        }

        try
        {
            manager.StartClient();
            Debug.Log($"Started client for {matchmakerAddress}:7777");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to start client: {e.Message}");
            yield break;
        }

        Debug.Log($"Created room: {roomName} on {matchmakerAddress}:7777");
    }
}