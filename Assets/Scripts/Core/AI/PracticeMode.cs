using System.Collections;
using Mirror;
using UnityEngine;

public class PracticeMode : MonoBehaviour
{
    public static PracticeMode Instance;

    [Header("Bot")]
    [SerializeField] private string botName = "StealthDragon AI";

    [Tooltip("Seconds to wait for the human's game player to spawn before giving up.")]
    [SerializeField] private float playerSpawnTimeout = 15f;

    public static bool Active { get; private set; }

    public static bool ForceRemoteBrain;

    [Tooltip("How well the built-in practice bot plays.")]
    [SerializeField] private BotSkill skill = BotSkill.Normal;

    private const string RemoteBrainFlag = "-remotebot";

    private static PlayerIdentity botIdentity;

    public static PlayerIdentity BotIdentity
    {
        get
        {
            if (botIdentity == null)
                botIdentity = PlayerIdentity.LoadOrCreate(
                    System.IO.Path.Combine(Application.persistentDataPath, "bot.key"));

            return botIdentity;
        }
    }

    private bool botSpawned;

    private static int savedMinPlayers = -1;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void StartPractice()
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.LogWarning("PracticeMode: a session is already running - stop it before starting practice.");
            return;
        }

        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton;
        if (manager == null)
        {
            Debug.LogError("PracticeMode: XSTDragonNetworkManager.singleton is null - cannot start practice.");
            return;
        }

        Active = true;
        botSpawned = false;
        if (savedMinPlayers < 0) savedMinPlayers = manager.minPlayers;
        manager.minPlayers = 1;

        Debug.LogWarning("PracticeMode: starting an offline practice match. No stake is taken and no payout is ever sent.");
        manager.StartHost();
        StartCoroutine(AutoReadyThenBegin(manager));
    }

    private IEnumerator AutoReadyThenBegin(XSTDragonNetworkManager manager)
    {
        float deadline = Time.realtimeSinceStartup + playerSpawnTimeout;

        while (Time.realtimeSinceStartup < deadline && CountRoomPlayers(manager) == 0)
            yield return null;

        if (CountRoomPlayers(manager) == 0)
        {
            Debug.LogError("PracticeMode: no room player appeared; cannot start the practice match.");
            yield break;
        }

        manager.minPlayers = 1;

        int readied = 0;
        foreach (NetworkRoomPlayer slot in manager.roomSlots)
        {
            if (slot == null || !slot.isOwned) continue;
            slot.CmdChangeReadyState(true);
            readied++;
        }

        if (readied == 0)
        {
            Debug.LogError("PracticeMode: no locally owned room player to ready up; cannot start the practice match.");
            yield break;
        }

        Debug.Log("PracticeMode: auto-readied " + readied + " local room player(s).");
    }

    private int CountRoomPlayers(XSTDragonNetworkManager manager)
    {
        int count = 0;
        foreach (NetworkRoomPlayer slot in manager.roomSlots)
            if (slot != null) count++;
        return count;
    }

    private static bool restartWanted;
    private static float restartAsked;

    public void Restart()
    {
        restartWanted = true;
        restartAsked = Time.realtimeSinceStartup;
        Time.timeScale = 1f;

        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton;

        if (manager != null && (NetworkServer.active || NetworkClient.active)) manager.StopHost();
    }

    void Update()
    {
        if (!restartWanted) return;
        if (NetworkServer.active || NetworkClient.active) return;
        if (Time.realtimeSinceStartup - restartAsked < 1f) return;

        restartWanted = false;

        Clear();
        StartPractice();
    }

    public static void Clear()
    {
        Active = false;

        if (savedMinPlayers < 0) return;

        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton;
        if (manager != null)
        {
            manager.minPlayers = savedMinPlayers;
            Debug.Log($"PracticeMode: restored minPlayers to {savedMinPlayers}.");
        }

        savedMinPlayers = -1;
    }

    public void OnGameplaySceneLoaded()
    {
        if (!Active) return;
        if (ReplayMatch.Active) return;
        if (botSpawned) return;
        botSpawned = true;
        StartCoroutine(SpawnBotThenStart());
    }

    private IEnumerator SpawnBotThenStart()
    {
        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton;
        GameManager gameManager = null;
        Player human = null;

        float deadline = Time.realtimeSinceStartup + playerSpawnTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameManager = FindFirstObjectByType<GameManager>();
            human = FindHumanPlayer();
            if (gameManager != null && human != null && human.deck != null) break;
            yield return null;
        }

        if (gameManager == null || human == null)
        {
            Debug.LogError("PracticeMode: gave up waiting for the GameManager and human player; practice match aborted.");
            yield break;
        }

        gameManager.practiceMode = true;

        Player bot = SpawnBot(manager);
        if (bot == null) yield break;

        yield return null;

        bot.deck.ServerLoadDeck();

        Debug.Log($"PracticeMode: bot {bot.username} dealt {bot.deck.hand.Count} cards, {bot.deck.deckList.Count} left in deck.");

        human.UpdateEnemyInfo();

        gameManager.StartGameForPlayer(human.netIdentity);
        Debug.Log($"PracticeMode: practice match started, {human.username} goes first.");
    }

    private Player FindHumanPlayer()
    {
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            Player p = conn.identity.GetComponent<Player>();
            if (p != null) return p;
        }
        return null;
    }

    private Player SpawnBot(XSTDragonNetworkManager manager)
    {
        if (manager.playerPrefab == null)
        {
            Debug.LogError("PracticeMode: playerPrefab is not assigned on the network manager - cannot spawn a bot.");
            return null;
        }

        GameObject botObject = Instantiate(manager.playerPrefab);
        Player bot = botObject.GetComponent<Player>();
        if (bot == null)
        {
            Debug.LogError("PracticeMode: playerPrefab has no Player component - cannot spawn a bot.");
            Destroy(botObject);
            return null;
        }

        bot.username = botName;
        bot.publicKey = BotIdentity.PublicKeyHex;
        botObject.name = botName;

        NetworkServer.Spawn(botObject);

        if (RemoteBrainWanted()) DriveRemotely(botObject, bot);
        else DriveLocally(botObject, bot);

        Debug.Log($"PracticeMode: spawned bot {bot.username} with netId {bot.netId}.");
        return bot;
    }

    private void DriveLocally(GameObject botObject, Player bot)
    {
        AIBot brain = botObject.GetComponent<AIBot>();
        if (brain == null) brain = botObject.AddComponent<AIBot>();
        brain.ServerInitialize(bot);
    }

    private void DriveRemotely(GameObject botObject, Player bot)
    {
        AIBot local = botObject.GetComponent<AIBot>();
        if (local != null) Destroy(local);

        RemoteBrain brain = botObject.GetComponent<RemoteBrain>();
        if (brain == null) brain = botObject.AddComponent<RemoteBrain>();

        IBotChannel channel = MatchBots.Open(0);
        bool entrant = channel != null;

        if (channel == null)
        {
            channel = new BuiltInBotChannel(skill, BotIdentity);
            Debug.Log($"PracticeMode: no bot dialled in - using the built-in " +
                      $"{BuiltInBotChannel.NameOf(skill)} policy.");
        }

        if (!string.IsNullOrEmpty(channel.Key)) bot.publicKey = channel.Key;

        if (entrant && !string.IsNullOrEmpty(channel.Name)) bot.username = channel.Name;

        brain.ServerInitialize(bot, channel);
    }

    private static bool RemoteBrainWanted()
    {
        if (ForceRemoteBrain) return true;

        foreach (string arg in System.Environment.GetCommandLineArgs())
            if (string.Equals(arg, RemoteBrainFlag, System.StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
