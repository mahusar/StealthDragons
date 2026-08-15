using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TwoClientFairnessHarness
{
    private const string RunPref = "xstd_fairness_run";
    private const string RigPref = "xstd_fairness_rig";
    private const string Address = "127.0.0.1";

    private static bool Rigged { get { return EditorPrefs.GetBool(RigPref, false); } }

    private static bool reported;
    private static bool readied;
    private static bool ended;
    private static double endAt;
    private static double dealtAt;
    private static double startedAt;

    private static bool IsClone { get { return Application.dataPath.Contains("_clone_"); } }

    private static string Tag { get { return IsClone ? "CLONEREPORT" : "HOSTREPORT"; } }

    static TwoClientFairnessHarness()
    {
        if (EditorPrefs.GetBool(RunPref, false))
            EditorApplication.update += Tick;
    }

    public static void LaunchAsClient()
    {
        EditorPrefs.SetBool(RunPref, true);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    public static void ArmHost()
    {
        ArmHost(false);
    }

    public static void ArmHost(bool rigged)
    {
        EditorPrefs.SetBool(RunPref, true);
        EditorPrefs.SetBool(RigPref, rigged);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    public static void Disarm()
    {
        EditorPrefs.DeleteKey(RunPref);
        EditorPrefs.DeleteKey(RigPref);
        EditorApplication.update -= Tick;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || reported) return;

        NetworkManager manager = NetworkManager.singleton;
        if (manager == null) return;

        if (startedAt == 0d) startedAt = EditorApplication.timeSinceStartup;

        if (EditorApplication.timeSinceStartup - startedAt > 900d)
        {
            Report("timed out before the match reached a reveal");
            return;
        }

        if (IsClone) TickClone(manager);
        else TickHost(manager);
    }

    private static void TickHost(NetworkManager manager)
    {
        if (!NetworkServer.active)
        {
            Debug.Log("[harness] starting host");
            manager.StartHost();
            return;
        }

        ReadyLocalRoomPlayer();

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
        if (gameManager == null) return;

        if (!ended)
        {
            if (gameManager.currentTurnNetId == 0) return;

            List<Player> players = LivePlayers();
            if (players.Count < 2) return;

            if (dealtAt == 0d) { dealtAt = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - dealtAt < 8d) return;

            ended = true;
            endAt = EditorApplication.timeSinceStartup;

            if (Rigged) RigTheServerSeed();

            Debug.Log($"[harness] forcing the match to end, {players[0].username} beats {players[1].username}");
            gameManager.ServerEndMatch(players[0], players[1], "defeat");
            return;
        }

        if (EditorApplication.timeSinceStartup - endAt > 3d) Report("host side");
    }

    private static void TickClone(NetworkManager manager)
    {
        if (!NetworkClient.active)
        {
            manager.networkAddress = Address;
            Debug.Log("[harness] clone connecting to " + Address);
            manager.StartClient();
            return;
        }

        if (!NetworkClient.isConnected) return;

        ReadyLocalRoomPlayer();

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
        if (gameManager == null || string.IsNullOrEmpty(gameManager.shuffleReveal)) return;

        if (endAt == 0d) { endAt = EditorApplication.timeSinceStartup; return; }

        if (EditorApplication.timeSinceStartup - endAt > 3d) Report("clone side");
    }

    private static void RigTheServerSeed()
    {
        System.Reflection.FieldInfo seed = typeof(MatchFairness).GetField(
            "serverSeed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (seed == null)
        {
            Debug.LogError("[harness] could not reach MatchFairness.serverSeed - the rig did nothing");
            return;
        }

        seed.SetValue(null, CardShuffle.NewSeed(CardShuffle.SeedBytes));
        Debug.Log("[harness] RIGGING: swapped the server seed for one it never committed to");
    }

    private static void ReadyLocalRoomPlayer()
    {
        if (readied) return;

        foreach (NetworkRoomPlayer room in Object.FindObjectsByType<NetworkRoomPlayer>(FindObjectsSortMode.None))
        {
            if (!room.isLocalPlayer || room.readyToBegin) continue;

            Debug.Log("[harness] readying the local room player");
            room.CmdChangeReadyState(true);
            readied = true;
        }
    }

    private static List<Player> LivePlayers()
    {
        List<Player> players = new List<Player>();

        foreach (Player entry in Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            if (entry != null && entry.health > 0) players.Add(entry);

        return players;
    }

    private static void Report(string where)
    {
        reported = true;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("================ " + Tag + " (" + where + ") ================");
        sb.AppendLine("isServer                 " + NetworkServer.active);
        sb.AppendLine("identity file            " + PlayerIdentity.MinePath());
        sb.AppendLine("my public key            " + PlayerIdentity.Mine.PublicKeyHex);

        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null)
        {
            sb.AppendLine("no GameManager on this side");
            Debug.Log(sb.ToString());
            return;
        }

        sb.AppendLine("seed commitments         " + Count(gm.shuffleSeedCommitments) + "   [2]");
        sb.AppendLine("seeds mixed in           " + Count(gm.shuffleContributions) + "   [2]");
        sb.AppendLine("dealt-order attestations " + Count(gm.shuffleDeals) + "   [2]");
        sb.AppendLine("shuffle verdict passed   " + LocalShuffleProof.Passed + "   [True]");
        sb.AppendLine("hands left unverified    " + LocalShuffleProof.Unverified + "   [0]");
        sb.AppendLine("verdict                  " + LocalShuffleProof.Result);

        MatchReceipt receipt = MatchReceipt.Parse(gm.matchReceipt);
        if (receipt == null)
        {
            sb.AppendLine("receipt                  MISSING OR UNREADABLE");
        }
        else
        {
            int humans = 0;
            foreach (MatchReceipt.Seat seat in receipt.seats) if (!seat.bot) humans++;

            sb.AppendLine("receipt digest           " + receipt.DigestHex());
            sb.AppendLine("receipt seats            " + receipt.seats.Count + " (" + humans + " human)   [2 (2 human)]");
            sb.AppendLine("receipt signatures       " + Count(gm.matchReceiptSignatures) + "   [2]");
            sb.AppendLine("receipt fully signed     " + gm.ReceiptFullySigned() + "   [True]");
            sb.AppendLine("receipt contested        " + receipt.Contested + "   [True]");
            sb.AppendLine();
            sb.AppendLine(gm.matchReceipt);
        }

        sb.AppendLine("================ end " + Tag + " ================");

        Debug.Log(sb.ToString());
    }

    private static int Count(string pairs)
    {
        if (string.IsNullOrEmpty(pairs)) return 0;

        return pairs.Split(';').Length;
    }
}
