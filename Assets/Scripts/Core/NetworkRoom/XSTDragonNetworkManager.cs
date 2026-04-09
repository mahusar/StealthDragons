using Mirror;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class XSTDragonNetworkManager : NetworkRoomManager
{
    public int networkPort = 7780;
    public string matchmakerAddress = "127.0.0.1";
    public int matchmakerPort = 5555;
    private bool isHosting = false;

    private int connectedPlayers = 0;
    private string statusFilePath => Application.persistentDataPath + "/status.txt";

    public static new XSTDragonNetworkManager singleton { get; private set; }

    private void WriteStatus()
    {
        File.WriteAllText(statusFilePath, connectedPlayers.ToString());
        Debug.Log($"Status written: {connectedPlayers}");
    }

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
            networkPort = 7780;
            UpdateTransportPort();
            StartServer();
        }
    }

    // ── Port helpers ──────────────────────────────────────────────────────────

    public void UpdateTransportPort()
    {
        if (Transport.active is TelepathyTransport telepathy)
        {
            telepathy.port = (ushort)networkPort;
            Debug.Log($"Transport port set to {networkPort}");
        }
    }

    public bool TryBindAvailablePort()
    {
        for (int port = 7780; port <= 7877; port++)
        {
            if (IsPortAvailable(port))
            {
                networkPort = port;
                UpdateTransportPort();
                Debug.Log($"Bound to port {networkPort}");
                return true;
            }
        }
        Debug.LogError("No available ports in range 7780–7877.");
        return false;
    }

    private bool IsPortAvailable(int port)
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(
                System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Server callbacks ──────────────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        isHosting = true;
        networkPort = 7780;
        connectedPlayers = 0;
        WriteStatus();
        UpdateTransportPort();
        Debug.Log($"Server started on {networkAddress}:{networkPort}");
    }

    public override void OnRoomServerSceneChanged(string sceneName)
    {
        base.OnRoomServerSceneChanged(sceneName);
        Debug.Log($"Server scene changed to: {sceneName}");

        if (sceneName == RoomScene)
        {
            var managers = FindObjectsOfType<NetworkManager>();
            if (managers.Length > 1)
            {
                for (int i = 1; i < managers.Length; i++)
                    Destroy(managers[i].gameObject);
            }
        }
        else if (sceneName == GameplayScene)
        {
            // Wait for all clients to finish loading before initializing match
            StartCoroutine(WaitForPlayersReadyThenInitWallet());
        }
    }

    private IEnumerator WaitForPlayersReadyThenInitWallet()
    {
        Debug.Log("[DragonatorWallet] Waiting for players to load scene...");

        // Wait until at least 1 connection has loaded the game scene
        yield return new WaitForSeconds(2f);

        var players = new System.Collections.Generic.List<NetworkConnectionToClient>(
            NetworkServer.connections.Values);

        Debug.Log($"[DragonatorWallet] Initializing match with {players.Count} players.");

        DragonatorWallet wallet = FindFirstObjectByType<DragonatorWallet>();
        if (wallet != null)
            wallet.InitializeMatch(players);
        else
            Debug.LogError("[DragonatorWallet] Not found in DragonMatch scene!");
    }

    public override void OnRoomServerPlayersReady()
    {
        Debug.Log("All players ready — starting DragonMatch");
        ServerChangeScene(GameplayScene);
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (roomSlots.Count >= 2) { conn.Disconnect(); return; }
        base.OnServerAddPlayer(conn);
        connectedPlayers++;
        WriteStatus();
        Debug.Log($"[Status] Player added, count: {connectedPlayers}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnServerDisconnect(conn);
        connectedPlayers = Mathf.Max(0, connectedPlayers - 1);
        WriteStatus();
        Debug.Log($"[Status] Player left, count: {connectedPlayers}");
    }

    public override void OnStopServer()
    {
        isHosting = false;
        SendToMatchmaker($"DEREGISTER|{networkAddress}|{networkPort}");
        base.OnStopServer();
        Debug.Log("Server stopped.");
        SceneManager.LoadScene("RoomOffline");
    }

    // ── Client callbacks ──────────────────────────────────────────────────────

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("Client connected to server.");
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log("Client disconnected.");

        DisconnectUI disconnectUI = FindFirstObjectByType<DisconnectUI>();
        if (disconnectUI != null)
            disconnectUI.ShowDisconnectMessage("Player Disconnected");
        else
            Debug.LogWarning("DisconnectUI not found.");

        if (!NetworkServer.active)
            SceneManager.LoadScene("RoomOffline");
    }

    // ── Matchmaker communication (TCP) ────────────────────────────────────────

    public void SendToMatchmaker(string message)
    {
        try
        {
            using (var client = new TcpClient())
            {
                client.Connect(matchmakerAddress, matchmakerPort);
                using (var stream = client.GetStream())
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    writer.WriteLine(message);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to send to matchmaker: {e.Message}");
        }
    }

    private void SendHeartbeat()
    {
        if (isHosting)
            SendToMatchmaker($"PING|{networkAddress}|{networkPort}");
    }
}