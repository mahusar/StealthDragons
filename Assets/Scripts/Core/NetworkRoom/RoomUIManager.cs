using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomUIManager : MonoBehaviour
{
    public TMP_Text playerListText;
    public Button readyButton;
    public Button startGameButton;
 //   public Button leaveRoomButton;

    private XSTDragonNetworkManager manager;
    private DragonRoomPlayer localRoomPlayer;
    public static RoomUIManager Instance;

    void Awake()
    {
        Instance = this;

        // Check for headless mode first
        if (Utils.IsHeadless())
        {
            Debug.Log("Headless mode detected, disabling RoomUIManager.");
            gameObject.SetActive(false);
            return;
        }

        // Find NetworkManager
        if (NetworkManager.singleton == null)
        {
            Debug.LogError("NetworkManager.singleton is null. Ensure a NetworkManager with XSTDragonNetworkManager is active in RoomOnline.");
            gameObject.SetActive(false);
            return;
        }

        manager = NetworkManager.singleton as XSTDragonNetworkManager;
        if (manager == null)
        {
            Debug.LogError("XSTDragonNetworkManager component not found on NetworkManager.");
            gameObject.SetActive(false);
            return;
        }

        // Check scene
        if (!Utils.IsSceneActive(manager.RoomScene))
        {
            Debug.LogWarning($"Current scene is not {manager.RoomScene}. Disabling RoomUIManager.");
            gameObject.SetActive(false);
            return;
        }
    }

    void Start()
    {
        if (readyButton != null)
            readyButton.onClick.AddListener(OnReadyClicked);
        else
            Debug.LogWarning("Ready Button not assigned in RoomUIManager.");

        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameClicked);
        else
            Debug.LogWarning("Start Game Button not assigned in RoomUIManager.");

    //    if (leaveRoomButton != null)
    //        leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
    //    else
    //        Debug.LogWarning("Leave Room Button not assigned in RoomUIManager.");

        if (startGameButton != null)
            startGameButton.gameObject.SetActive(false);
    }

    void Update()
    {
        if (localRoomPlayer == null && NetworkClient.localPlayer != null)
        {
            localRoomPlayer = NetworkClient.localPlayer.GetComponent<DragonRoomPlayer>();
            UpdateReadyButtonText();
        }

        UpdatePlayerList();

        if (manager != null && manager.allPlayersReady && NetworkServer.active && startGameButton != null)
        {
            startGameButton.gameObject.SetActive(true);
        }
        else if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(false);
        }
    }

    public void UpdatePlayerList()
    {
        if (playerListText == null || manager == null)
        {
            Debug.LogWarning("Player list text or manager is null.");
            return;
        }

        string playerList = "Players:\n";
        if (manager.roomSlots == null || manager.roomSlots.Count == 0)
        {
            playerList += "No players in room.\n";
        }
        else
        {
            foreach (var player in manager.roomSlots)
            {
                var roomPlayer = player as DragonRoomPlayer;
                if (roomPlayer != null)
                {
                    playerList += $"Player {roomPlayer.index + 1}: {(roomPlayer.readyToBegin ? "Ready" : "Not Ready")}\n";
                }
            }
        }
        playerListText.text = playerList;
    }

    void OnReadyClicked()
    {
        if (localRoomPlayer == null)
        {
            Debug.LogWarning("Local player not found.");
            return;
        }

        bool nextState = !localRoomPlayer.readyToBegin;
        localRoomPlayer.CmdChangeReadyState(nextState);
        UpdateReadyButtonText();
    }

    void UpdateReadyButtonText()
    {
        if (readyButton == null || localRoomPlayer == null)
        {
            Debug.LogWarning("Ready button or local player is null.");
            return;
        }

        var text = readyButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (text != null)
            text.text = localRoomPlayer.readyToBegin ? "Cancel Ready" : "Ready";
        else
            Debug.LogWarning("Ready button text component not found.");
    }

    void OnStartGameClicked()
    {
        if (manager != null && manager.allPlayersReady && NetworkServer.active)
        {
            Debug.Log("Starting game from RoomUIManager.");
            manager.ServerChangeScene(manager.GameplayScene);
        }
    }
    /*
    void OnLeaveRoomClicked()
    {
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            Debug.Log("Host leaving room - stopping host");
            manager.StopHost();
        }
        else if (NetworkClient.isConnected)
        {
            Debug.Log("Client leaving room - stopping client only");
            manager.StopClient();
        }
    }
    */
}