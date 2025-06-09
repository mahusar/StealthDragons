using UnityEngine;
using Mirror;
using System.Net.Sockets;
using System.Text;
using UnityEngine.SceneManagement;

public class XSTDragonNetworkManager : NetworkRoomManager
{
    public int networkPort = 7777;
    private bool isHosting = false;
    public string matchmakerAddress = "127.0.0.1";
    public int matchmakerPort = 5555;
    public static new XSTDragonNetworkManager singleton { get; private set; }

    public override void Awake()
    {
        if (singleton != null && singleton != this)
        {
            Debug.LogWarning("Duplicate XSTDragonNetworkManager detected. Destroying this instance.");
            Destroy(gameObject);
            return;
        }
        singleton = this;
        DontDestroyOnLoad(gameObject);

        base.Awake();

        if (string.IsNullOrEmpty(RoomScene))
            RoomScene = "RoomOnline";
        if (string.IsNullOrEmpty(GameplayScene))
            GameplayScene = "DragonMatch";

        Debug.Log($"XSTDragonNetworkManager initialized. RoomScene: {RoomScene}, GameplayScene: {GameplayScene}");

        if (Utils.IsHeadless())
        {
            networkAddress = matchmakerAddress;
            UpdateTransportPort();
            StartServer();
        }
    }

    public void UpdateTransportPort()
    {
        if (Transport.active is TelepathyTransport telepathy)
        {
            telepathy.port = (ushort)networkPort;
            Debug.Log($"Transport port set to {networkPort}");
        }
    }

    public override void OnStartServer()
    {
        bool portAssigned = false;
        for (int port = 7777; port <= 7877; port++)
        {
            networkPort = port;
            UpdateTransportPort();
            try
            {
                base.OnStartServer();
                portAssigned = true;
                Debug.Log($"Server started on {networkAddress}:{networkPort}");
                Debug.Log($"Server: listening port={networkPort}");
                break;
            }
            catch
            {
                Debug.LogWarning($"Port {port} in use, trying next...");
            }
        }

        if (!portAssigned)
        {
            Debug.LogError("No available ports in range 7777–7877.");
            return;
        }
        
    }

    public override void OnRoomServerSceneChanged(string sceneName)
    {
        base.OnRoomServerSceneChanged(sceneName);
        Debug.Log($"Server scene changed to: {sceneName}");
        if (sceneName == "RoomOnline")
        {
            var managers = FindObjectsOfType<NetworkManager>();
            if (managers.Length > 1)
            {
                Debug.LogError($"Multiple NetworkManagers detected ({managers.Length}). Keeping only singleton.");
                for (int i = 1; i < managers.Length; i++)
                {
                    Destroy(managers[i].gameObject);
                }
            }
        }
    }

    public override void OnRoomServerPlayersReady()
    {
        Debug.Log("All players ready, starting DragonMatch");
        ServerChangeScene(GameplayScene);
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (roomSlots.Count >= 2)
        {
            Debug.Log("Room full (max 2 players). Rejecting connection.");
            conn.Disconnect();
            return;
        }
        base.OnServerAddPlayer(conn);
        SendToMatchmaker($"PLAYER_JOIN|{matchmakerAddress}|{networkPort}");
        Debug.Log($"Player added: {conn.connectionId}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        Debug.Log($"Player with connectionId {conn.connectionId} disconnected.");
        GameManager gameManager = FindObjectOfType<GameManager>();
        if (gameManager != null)
        {
            gameManager.ShowDisconnectMessageOnClients("Opponent Disconnected");
            Debug.Log("Notified GameManager to show 'Opponent Disconnected'.");
        }
        else
        {
            Debug.LogWarning("GameManager not found, cannot notify clients of disconnection.");
        }
        base.OnServerDisconnect(conn);
        SendToMatchmaker($"PLAYER_LEAVE|{networkAddress}|{networkPort}");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("Client connected to server.");
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log("Client disconnected from server, showing disconnect message.");
        DisconnectUI disconnectUI = FindObjectOfType<DisconnectUI>();
        if (disconnectUI != null)
        {
            disconnectUI.ShowDisconnectMessage("Player Disconnected");
            Debug.Log("Client: Displayed 'Player Disconnected' message.");
        }
        else
        {
            Debug.LogWarning("Client: DisconnectUI not found, cannot display message.");
        }

        if (!NetworkServer.active)
        {
            SceneManager.LoadScene("RoomOffline");
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log("Server stopping gracefully.");
        SceneManager.LoadScene("RoomOffline");
    }

    private void SendHeartbeat()
    {
        if (isHosting)
        {
            SendToMatchmaker($"PING|{matchmakerAddress}|{networkPort}");
        }
    }

    public void SendToMatchmaker(string message)
    {
        try
        {
            using (var client = new UdpClient())
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                client.Send(data, data.Length, matchmakerAddress, matchmakerPort);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to send to matchmaker: {e.Message}");
        }
    }
}