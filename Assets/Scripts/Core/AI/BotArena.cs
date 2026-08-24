using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class BotArena : MonoBehaviour
{
    public static BotArena Instance;

    private const string ForceFlag = "-arena";

    [Tooltip("Seconds to wait for the gameplay scene and its GameManager before giving up.")]
    [SerializeField] private float sceneTimeout = 20f;

    [Tooltip("Seconds between one match ending and the next starting.")]
    [SerializeField] private float restSeconds = 5f;

    [Tooltip("Seconds to wait for every seat to sign the match receipt before moving on.")]
    [SerializeField] private float signatureWait = 10f;

    public static bool Active { get; private set; }

    public static int Played { get; private set; }

    private const float ShortestSaneMatch = 5f;
    private const int MaxShortMatches = 3;

    private readonly List<Player> seated = new List<Player>();
    private readonly IBotChannel[] channels = new IBotChannel[2];

    private bool running;
    private int shortMatches;
    private int lastGameManager;
    private int announcedWaiting = -1;

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

    public static bool Wanted()
    {
        if (HasFlag(ForceFlag)) return true;

        return Utils.IsHeadless() && MatchBots.Seats >= 2;
    }

    public void ServerBegin()
    {
        if (running) return;
        if (!NetworkServer.active)
        {
            Debug.LogError("BotArena: the server is not running - no arena match can start.");
            return;
        }

        running = true;
        Active = true;

        Debug.Log("BotArena: this server plays bot against bot. No human seat is offered.");

        StartCoroutine(RunMatches());
    }

    private IEnumerator RunMatches()
    {
        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton;
        if (manager == null)
        {
            Debug.LogError("BotArena: no network manager - the arena cannot run.");
            yield break;
        }

        while (running)
        {
            yield return WaitForEntrants();

            if (!running) yield break;

            float began = Time.realtimeSinceStartup;

            yield return RunOneMatch(manager);

            if (!running) yield break;

            float lasted = Time.realtimeSinceStartup - began;

            Played++;
            Debug.Log($"BotArena: {Played} match(es) played, the last took {lasted:0.#}s.");

            if (lasted < ShortestSaneMatch)
            {
                shortMatches++;

                if (shortMatches >= MaxShortMatches)
                {
                    Debug.LogError($"BotArena: {shortMatches} matches in a row ended in under " +
                                   $"{ShortestSaneMatch:0.#}s. Something is wrong, so the arena is stopping " +
                                   "rather than looping.");
                    running = false;
                    Active = false;
                    yield break;
                }
            }
            else shortMatches = 0;

            yield return new WaitForSeconds(restSeconds);
        }
    }

    private IEnumerator WaitForEntrants()
    {
        if (!MatchBots.Installed || MatchBots.Seats < 2) yield break;

        while (running && MatchBots.Waiting < 2)
        {
            int waiting = MatchBots.Waiting;

            if (waiting != announcedWaiting)
            {
                announcedWaiting = waiting;
                Debug.Log($"BotArena: waiting for bots to dial in - {waiting} of 2 connected.");
            }

            yield return new WaitForSeconds(1f);
        }

        announcedWaiting = -1;
    }

    private IEnumerator RunOneMatch(XSTDragonNetworkManager manager)
    {
        seated.Clear();
        channels[0] = null;
        channels[1] = null;

        manager.ServerChangeScene(manager.GameplayScene);

        GameManager gameManager = null;
        float deadline = Time.realtimeSinceStartup + sceneTimeout;

        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;

            GameManager found = FindFirstObjectByType<GameManager>();
            if (found == null) continue;

            if (found.GetInstanceID() == lastGameManager) continue;

            gameManager = found;
            lastGameManager = found.GetInstanceID();
            break;
        }

        if (gameManager == null)
        {
            Debug.LogError("BotArena: the gameplay scene never produced a fresh GameManager; " +
                           "stopping the arena.");
            running = false;
            Active = false;
            yield break;
        }

        gameManager.practiceMode = true;

        for (int seat = 0; seat < 2; seat++)
        {
            Player player = SpawnSeat(manager, seat);
            if (player == null)
            {
                Debug.LogError("BotArena: a seat could not be filled; stopping the arena.");
                running = false;
                Active = false;
                yield break;
            }

            seated.Add(player);
        }

        yield return null;

        foreach (Player player in seated)
        {
            RemoteBrain brain = player.GetComponent<RemoteBrain>();
            if (brain != null) yield return brain.ServerChooseDeck();
        }

        foreach (Player player in seated)
        {
            if (player.deck != null) player.deck.ServerLoadDeck();
            player.UpdateEnemyInfo();
        }

        Debug.Log($"BotArena: {seated[0].username} against {seated[1].username}.");

        gameManager.StartGameForPlayer(seated[0].netIdentity);

        while (running && !Finished(gameManager)) yield return null;

        yield return WaitForSignatures(gameManager);

        for (int seat = 0; seat < 2; seat++)
        {
            if (channels[seat] == null) continue;

            try
            {
                channels[seat].Close(Outcome(seat));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"BotArena: seat {seat + 1} threw while being released ({e.GetType().Name}).");
            }

            channels[seat] = null;
        }
    }

    private IEnumerator WaitForSignatures(GameManager gameManager)
    {
        if (gameManager == null) yield break;

        float deadline = Time.realtimeSinceStartup + signatureWait;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (gameManager == null) yield break;
            if (gameManager.ReceiptFullySigned()) yield break;

            yield return null;
        }

        Debug.LogWarning($"BotArena: the match receipt was still not fully signed after " +
                         $"{signatureWait:0.#}s - recording it as it stands.");
    }

    private string Outcome(int seat)
    {
        if (seat < 0 || seat >= seated.Count) return "";

        Player self = seated[seat];
        if (self == null) return "loss";

        Player other = seated[seat == 0 ? 1 : 0];
        if (other == null) return "win";

        if (self.health <= 0 && other.health <= 0) return "draw";
        if (self.health <= 0) return "loss";
        if (other.health <= 0) return "win";

        return "draw";
    }

    private bool Finished(GameManager gameManager)
    {
        if (gameManager == null) return true;

        foreach (Player player in seated)
            if (player == null || player.health <= 0) return true;

        return false;
    }

    private Player SpawnSeat(XSTDragonNetworkManager manager, int seat)
    {
        if (manager.playerPrefab == null)
        {
            Debug.LogError("BotArena: playerPrefab is not assigned on the network manager.");
            return null;
        }

        IBotChannel channel = MatchBots.Open(seat);

        if (channel == null)
        {
            BotSkill skill = seat == 0 ? BotSkill.Easy : BotSkill.Normal;
            channel = new BuiltInBotChannel(skill, SeatIdentity(seat));

            Debug.LogWarning($"BotArena: no bot dialled in for seat {seat + 1} - " +
                             $"using the built-in {BuiltInBotChannel.NameOf(skill)} policy.");
        }

        channels[seat] = channel;

        GameObject seatObject = Instantiate(manager.playerPrefab);
        Player player = seatObject.GetComponent<Player>();

        if (player == null)
        {
            Debug.LogError("BotArena: playerPrefab has no Player component.");
            Destroy(seatObject);
            return null;
        }

        string name = channel.Name;
        if (string.IsNullOrEmpty(name)) name = "bot " + (seat + 1);

        string key = channel.Key;
        if (string.IsNullOrEmpty(key)) key = SeatIdentity(seat).PublicKeyHex;

        player.username = name;
        player.publicKey = key;
        seatObject.name = name;

        AIBot local = seatObject.GetComponent<AIBot>();
        if (local != null) Destroy(local);

        NetworkServer.Spawn(seatObject);

        RemoteBrain brain = seatObject.GetComponent<RemoteBrain>();
        if (brain == null) brain = seatObject.AddComponent<RemoteBrain>();
        brain.ServerInitialize(player, channel);

        return player;
    }

    private static PlayerIdentity SeatIdentity(int seat)
    {
        string file = "arena-seat-" + (seat + 1) + ".key";
        return PlayerIdentity.LoadOrCreate(System.IO.Path.Combine(Application.persistentDataPath, file));
    }

    private static bool HasFlag(string flag)
    {
        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
