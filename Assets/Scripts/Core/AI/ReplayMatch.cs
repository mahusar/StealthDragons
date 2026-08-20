using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class ReplayMatch : MonoBehaviour
{
    public static ReplayMatch Instance;

    public static bool Active { get; private set; }

    public static bool Finished { get; private set; }

    public static string Trouble { get; private set; }

    public static string Verdict { get; private set; }

    public static int TotalTurns { get; private set; }

    public static float Speed { get; private set; }

    public static bool Paused
    {
        get { return Time.timeScale == 0f; }
    }

    private static MatchReplay watched;

    [Tooltip("Seconds to wait for the gameplay scene and both seats before giving up.")]
    [SerializeField] private float sceneTimeout = 20f;

    [Tooltip("Seconds between actions at normal speed.")]
    [SerializeField] private float stepSeconds = 0.6f;

    private MatchReplay replay;
    private GameManager gameManager;

    private readonly Player[] seats = new Player[2];
    private readonly ReplayChannel[] channels = new ReplayChannel[2];

    private bool arranged;

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

    public static void Clear()
    {
        Active = false;
        Finished = false;
        Trouble = "";
        Verdict = "";
        Time.timeScale = 1f;
    }

    public bool Watch(MatchReplay watched)
    {
        if (watched == null || watched.seats.Count < 2)
        {
            Trouble = "this replay does not name two seats";
            return false;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            Trouble = "a match is already running - leave it before watching a replay";
            return false;
        }

        PracticeMode practice = FindFirstObjectByType<PracticeMode>();
        if (practice == null)
        {
            Trouble = "this build cannot host a local match to play the replay in";
            return false;
        }

        replay = watched;
        ReplayMatch.watched = watched;
        Active = true;
        Speed = 1f;

        int last = 0;
        foreach (MatchReplay.Move move in watched.playback)
            if (move.turn > last) last = move.turn;

        TotalTurns = last;

        Finished = false;
        arranged = false;
        Trouble = "";
        Verdict = "";
        Time.timeScale = 1f;

        Debug.Log("ReplayMatch: watching a replay of " + watched.playback.Count + " move(s).");

        practice.StartPractice();
        return true;
    }

    public void OnGameplaySceneLoaded()
    {
        if (!Active || arranged) return;
        arranged = true;

        StartCoroutine(Arrange());
    }

    private IEnumerator Arrange()
    {
        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton;

        float deadline = Time.realtimeSinceStartup + sceneTimeout;
        Player local = null;

        while (Time.realtimeSinceStartup < deadline)
        {
            gameManager = FindFirstObjectByType<GameManager>();
            local = FindLocalPlayer();

            if (gameManager != null && local != null && local.deck != null) break;
            yield return null;
        }

        if (gameManager == null || local == null)
        {
            Fail("the gameplay scene never produced a seat to watch from");
            yield break;
        }

        gameManager.practiceMode = true;

        Player other = SpawnSeat(manager);
        if (other == null) yield break;

        seats[0] = local;
        seats[1] = other;

        yield return null;

        if (!MatchFairness.BeginReplay(replay.seed, replay.mix))
        {
            Fail("the recorded seed could not be rebuilt, so the cards would not match");
            yield break;
        }

        gameManager.shuffleCommitment = MatchFairness.CommitmentHex;

        MatchReplay.Seat[] recorded = new MatchReplay.Seat[2];

        for (int seat = 0; seat < 2; seat++)
        {
            if (replay.TrySeatAt(seat, out recorded[seat])) continue;

            Fail("the replay never names seat " + seat + ", so its moves cannot be handed to a player");
            yield break;
        }

        for (int seat = 0; seat < 2; seat++)
        {
            MatchFairness.MapReplaySeat(seats[seat].netId, recorded[seat].netId);

            seats[seat].username = recorded[seat].username;
            seats[seat].publicKey = recorded[seat].publicKeyHex;
            seats[seat].gameObject.name = recorded[seat].username;
        }

        for (int seat = 0; seat < 2; seat++)
        {
            channels[seat] = new ReplayChannel(recorded[seat].username,
                                               recorded[seat].publicKeyHex,
                                               MovesFor(seat),
                                               Resolve);

            AIBot idle = seats[seat].GetComponent<AIBot>();
            if (idle != null) Destroy(idle);

            RemoteBrain brain = seats[seat].GetComponent<RemoteBrain>();
            if (brain == null) brain = seats[seat].gameObject.AddComponent<RemoteBrain>();

            brain.actionDelay = stepSeconds;
            brain.ServerInitialize(seats[seat], channels[seat]);
        }

        if (other.deck != null) other.deck.ServerLoadDeck();

        foreach (Player seat in seats) seat.UpdateEnemyInfo();

        int first = replay.playback.Count > 0 ? replay.playback[0].seat : 0;
        if (first < 0 || first > 1) first = 0;

        Debug.Log("ReplayMatch: " + seats[0].username + " against " + seats[1].username +
                  ", " + seats[first].username + " goes first.");

        gameManager.StartGameForPlayer(seats[first].netIdentity);

        StartCoroutine(WatchToTheEnd());
    }

    private IEnumerator WatchToTheEnd()
    {
        while (Active && !Finished)
        {
            if (gameManager == null) break;

            bool spent = true;

            foreach (ReplayChannel channel in channels)
            {
                if (channel == null) continue;

                if (channel.Diverged)
                {
                    Fail(channel.Trouble);
                    yield break;
                }

                if (channel.Remaining > 0) spent = false;
            }

            if (spent) break;

            yield return null;
        }

        yield return new WaitForSecondsRealtime(1f);

        Settle();
    }

    private void Settle()
    {
        Finished = true;
        Time.timeScale = 1f;

        string rebuilt = gameManager != null && gameManager.replay != null
            ? gameManager.replay.BodyDigestHex()
            : "";

        string original = replay != null ? replay.BodyDigestHex() : "";

        if (!string.IsNullOrEmpty(rebuilt) && rebuilt == original)
        {
            Verdict = "faithful";
            Debug.Log("ReplayMatch: the replay played back exactly, digest " + original.Substring(0, 16) + ".");
            return;
        }

        Verdict = "approximate";

        Debug.LogWarning("ReplayMatch: the replay finished but did not reproduce the recorded match exactly. " +
                         "recorded=" + Short(original) + " replayed=" + Short(rebuilt));
    }

    public static void SetSpeed(float multiplier)
    {
        Speed = Mathf.Clamp(multiplier, 0.25f, 8f);

        if (!Paused) Time.timeScale = Speed;
    }

    public static void Pause()
    {
        Time.timeScale = 0f;
    }

    public static void Resume()
    {
        if (Speed <= 0f) Speed = 1f;

        Time.timeScale = Speed;
    }

    public static void TogglePause()
    {
        if (Paused) Resume();
        else Pause();
    }

    private static MatchReplay pending;
    private static float pendingFrom;

    public bool Restart()
    {
        if (watched == null) return false;

        pending = watched;
        pendingFrom = Time.realtimeSinceStartup;

        Time.timeScale = 1f;
        Active = false;
        Finished = false;
        arranged = false;

        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton;

        if (manager != null && (NetworkServer.active || NetworkClient.active)) manager.StopHost();

        return true;
    }

    void Update()
    {
        if (pending == null || Active) return;
        if (NetworkServer.active || NetworkClient.active) return;
        if (Time.realtimeSinceStartup - pendingFrom < 1f) return;

        if (FindFirstObjectByType<PracticeMode>() == null) return;

        MatchReplay again = pending;
        pending = null;

        PracticeMode.Clear();

        if (!Watch(again)) Debug.LogWarning("ReplayMatch: the replay could not be restarted - " + Trouble);
    }

    private List<MatchReplay.Move> MovesFor(int seat)
    {
        List<MatchReplay.Move> mine = new List<MatchReplay.Move>();

        foreach (MatchReplay.Move move in replay.playback)
            if (move.seat == seat) mine.Add(move);

        return mine;
    }

    private uint Resolve(string stable)
    {
        if (string.IsNullOrEmpty(stable)) return 0;

        string heroPrefix = MatchReplay.Hero + ":";

        if (stable.StartsWith(heroPrefix))
        {
            int seat;
            if (!int.TryParse(stable.Substring(heroPrefix.Length), out seat)) return 0;
            if (seat < 0 || seat > 1 || seats[seat] == null) return 0;

            return seats[seat].netId;
        }

        if (gameManager == null || gameManager.replay == null) return 0;

        return gameManager.replay.LiveOf(stable);
    }

    private Player FindLocalPlayer()
    {
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null || conn.identity == null) continue;

            Player found = conn.identity.GetComponent<Player>();
            if (found != null) return found;
        }

        return null;
    }

    private Player SpawnSeat(XSTDragonNetworkManager manager)
    {
        if (manager == null || manager.playerPrefab == null)
        {
            Fail("playerPrefab is not assigned on the network manager");
            return null;
        }

        GameObject seatObject = Instantiate(manager.playerPrefab);
        Player player = seatObject.GetComponent<Player>();

        if (player == null)
        {
            Fail("playerPrefab has no Player component");
            Destroy(seatObject);
            return null;
        }

        NetworkServer.Spawn(seatObject);
        return player;
    }

    private static string Short(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return "none";

        return hex.Length <= 16 ? hex : hex.Substring(0, 16);
    }

    private void Fail(string reason)
    {
        Trouble = reason;
        Verdict = "failed";
        Active = false;
        Finished = true;
        Time.timeScale = arranged && gameManager != null ? 0f : 1f;

        Debug.LogError("ReplayMatch: " + reason + ".");
    }
}
