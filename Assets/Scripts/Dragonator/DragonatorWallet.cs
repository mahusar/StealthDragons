using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Mirror;
using UnityEngine;

public class DragonatorWallet : NetworkBehaviour, IMatchEscrowHost
{
    public static DragonatorWallet Instance;

    [Header("Mid-match disconnect")]
    [Tooltip("Seconds a player who drops mid-match has before they forfeit. Kept short because " +
             "the window cannot yet be used to rejoin - it is only a delay until session-token " +
             "reconnect exists. Set 0 to forfeit immediately.")]
    [SerializeField] private float forfeitGraceSeconds = 5f;

    [Tooltip("Seconds to wait for both clients to load the match scene on a free match.")]
    [SerializeField] private float freeMatchReadyTimeout = 15f;

    [SyncVar(hook = nameof(OnPlayer1StatusChanged))] private string player1Status = "";
    [SyncVar(hook = nameof(OnPlayer2StatusChanged))] private string player2Status = "";

    [SyncVar] private double fundingDeadline;
    public double FundingDeadline => fundingDeadline;

    [SyncVar] private double forfeitDeadline;
    public double ForfeitDeadline => forfeitDeadline;

    private class Seat
    {
        public NetworkConnectionToClient conn;
        public int connectionId;
        public int slot;
        public string name;
        public bool sceneReady;
        public bool disconnected;
        public bool promptPending;
        public string promptAmount;
        public int promptConfirmations;
    }

    private readonly Dictionary<int, Seat> seats = new Dictionary<int, Seat>();

    private readonly HashSet<NetworkConnectionToClient> earlyReadyConnections
        = new HashSet<NetworkConnectionToClient>();

    private IMatchEscrow escrow;
    private string matchId = "";
    private bool escrowActive;
    private bool escrowReady;
    private bool matchStarted;
    private bool settleRequested;
    private bool cancelled;
    private Coroutine forfeitWatch;

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[DragonatorWallet] A second instance appeared - the newest one wins.");
        Instance = this;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        escrow = AddonLoader.Escrow;

        if (escrow == null)
        {
            Debug.Log("[DragonatorWallet] No match escrow installed - matches are free: no stake is " +
                      "collected and no payout is ever sent.");
            return;
        }

        try
        {
            escrow.Attach(this);
            escrowActive = true;
        }
        catch (Exception e)
        {
            escrow = null;
            escrowActive = false;

            Debug.LogError($"[DragonatorWallet] The match escrow could not start ({e.GetType().Name}: {e.Message}).");

            if (Utils.IsHeadless())
            {
                Debug.LogError("[DragonatorWallet] An escrow add-on is installed, so this server was meant to " +
                               "handle stakes. Refusing to start rather than silently running free matches. " +
                               "Fix the wallet, or remove the add-on to run a free server.");
                Application.Quit(1);
                return;
            }

            Debug.LogWarning("[DragonatorWallet] Running free matches instead.");
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        if (escrow == null) return;

        try
        {
            escrow.Shutdown();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DragonatorWallet] The escrow threw while shutting down: {e.Message}");
        }

        escrowActive = false;
    }

    private static int LedgerPort()
    {
        return XSTDragonNetworkManager.singleton != null
            ? XSTDragonNetworkManager.singleton.networkPort
            : 0;
    }

    void Update()
    {
        if (!NetworkServer.active || escrow == null || !escrowActive) return;

        try
        {
            escrow.Tick();
        }
        catch (Exception e)
        {
            Debug.LogError($"[DragonatorWallet] The escrow threw during Tick ({e.GetType().Name}: {e.Message}). " +
                           "Voiding the match so nothing is left half-settled.");
            escrowActive = false;
            EscrowVoided(matchId, "The match could not be settled safely.");
        }
    }

    void OnPlayer1StatusChanged(string old, string newVal) =>
        BetUI.Instance?.UpdatePlayerStatus(1, newVal);

    void OnPlayer2StatusChanged(string old, string newVal) =>
        BetUI.Instance?.UpdatePlayerStatus(2, newVal);

    [Command(requiresAuthority = false)]
    public void CmdClientReady(NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;

        Seat seat;
        if (!seats.TryGetValue(sender.connectionId, out seat))
        {
            earlyReadyConnections.Add(sender);
            return;
        }

        seat.sceneReady = true;
        Debug.Log($"[DragonatorWallet] Client {sender.connectionId} is scene-ready.");
        FlushPrompts();
    }

    [Server]
    public void InitializeMatch(List<NetworkConnectionToClient> players)
    {
        seats.Clear();
        cancelled = false;
        matchStarted = false;
        escrowReady = false;
        settleRequested = false;

        matchId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        int slot = 1;
        foreach (NetworkConnectionToClient conn in players)
        {
            if (conn == null) continue;
            if (slot > 2)
            {
                Debug.LogWarning("[DragonatorWallet] More than 2 connections - ignoring the extras.");
                break;
            }

            string name = conn.identity?.GetComponent<Player>()?.username ?? $"Player {slot}";
            seats[conn.connectionId] = new Seat
            {
                conn = conn,
                connectionId = conn.connectionId,
                slot = slot,
                name = name
            };
            slot++;
        }

        foreach (NetworkConnectionToClient conn in earlyReadyConnections)
        {
            Seat early;
            if (seats.TryGetValue(conn.connectionId, out early)) early.sceneReady = true;
        }
        earlyReadyConnections.Clear();

        if (escrow == null)
        {
            fundingDeadline = 0;
            Debug.Log($"[DragonatorWallet] Match {matchId}: free match, {seats.Count} player(s), no stake.");
            StartCoroutine(WaitForSceneReadyThenBeginMatch());
            return;
        }

        int[] connectionIds = new int[seats.Count];
        string[] names = new string[seats.Count];
        int i = 0;
        foreach (Seat seat in seats.Values)
        {
            connectionIds[i] = seat.connectionId;
            names[i] = seat.name;
            i++;
        }

        escrowActive = true;
        escrow.BeginMatch(matchId, connectionIds, names);
    }

    [Server]
    private IEnumerator WaitForSceneReadyThenBeginMatch()
    {
        float deadline = Time.realtimeSinceStartup + freeMatchReadyTimeout;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (AllSceneReady() && SlotSeat(1)?.conn?.identity != null) break;
            yield return new WaitForSeconds(0.25f);
        }

        if (!AllSceneReady())
            Debug.LogWarning("[DragonatorWallet] Not every client reported scene-ready within " +
                             $"{freeMatchReadyTimeout}s - starting anyway.");

        RpcFreeMatchNotice();
        BeginMatch();
    }

    [Server]
    private bool AllSceneReady()
    {
        if (seats.Count == 0) return false;

        foreach (Seat seat in seats.Values)
            if (!seat.sceneReady) return false;

        return true;
    }

    [Server]
    private void FlushPrompts()
    {
        foreach (Seat seat in seats.Values)
        {
            if (!seat.promptPending || !seat.sceneReady) continue;

            seat.promptPending = false;
            TargetPromptPayoutAddress(seat.conn, seat.promptAmount, seat.promptConfirmations);
        }
    }

    [Command(requiresAuthority = false)]
    public void CmdSubmitPayoutAddress(string payoutAddress, NetworkConnectionToClient sender = null)
    {
        if (sender == null || !seats.ContainsKey(sender.connectionId))
        {
            Debug.LogWarning("[DragonatorWallet] Payout address from an unknown connection.");
            return;
        }

        if (escrow == null) return;

        escrow.SubmitPayoutAddress(matchId, sender.connectionId, payoutAddress);
    }

    [Server]
    private void BeginMatch()
    {
        if (matchStarted) return;
        matchStarted = true;

        RpcHideStatusDisplay();

        Seat first = SlotSeat(1);
        if (first?.conn?.identity == null)
        {
            Debug.LogError("[DragonatorWallet] Slot 1 has no player identity - cannot start match!");
            return;
        }

        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
            gameManager.StartGameForPlayer(first.conn.identity);
        else
            Debug.LogError("[DragonatorWallet] GameManager not found!");
    }

    public bool BothPlayersValidated()
    {
        if (escrow == null) return true;
        if (!NetworkServer.active) return false;

        return escrowReady;
    }

    [Server]
    public void NotifyPlayerDisconnected(NetworkConnectionToClient conn)
    {
        if (conn == null) return;

        Seat seat;
        if (!seats.TryGetValue(conn.connectionId, out seat)) return;

        seat.disconnected = true;

        if (escrow != null && !escrowReady)
        {
            escrow.PlayerLeft(matchId, conn.connectionId);
            return;
        }

        if (!matchStarted || settleRequested || cancelled) return;

        if (AllDisconnected())
        {
            VoidMatch("Both players disconnected - match voided.");
            return;
        }

        Debug.LogWarning($"[DragonatorWallet] {seat.name} dropped mid-match - " +
                         $"{forfeitGraceSeconds}s before forfeit.");

        if (forfeitWatch == null)
            forfeitWatch = StartCoroutine(ForfeitCountdown(seat));
    }

    [Server]
    private bool AllDisconnected()
    {
        if (seats.Count == 0) return false;

        foreach (Seat seat in seats.Values)
            if (!seat.disconnected) return false;

        return true;
    }

    [Server]
    private IEnumerator ForfeitCountdown(Seat leaver)
    {
        forfeitDeadline = NetworkTime.time + forfeitGraceSeconds;

        SetPlayerStatus(leaver.connectionId, "<color=#FF0000>Disconnected</color>");
        foreach (Seat seat in seats.Values)
        {
            if (seat.disconnected) continue;
            SetPlayerStatus(seat.connectionId, "<color=#FFAA00>Waiting for opponent...</color>");
            Message(seat.connectionId, false,
                    $"{leaver.name} disconnected. If they do not return you win the pot.");
        }
        RpcShowStatusDisplay();

        while (NetworkTime.time < forfeitDeadline)
        {
            if (AllDisconnected())
            {
                forfeitWatch = null;
                forfeitDeadline = 0;
                VoidMatch("Both players disconnected - match voided.");
                yield break;
            }
            yield return null;
        }

        forfeitDeadline = 0;
        forfeitWatch = null;

        Seat remaining = null;
        int stillHere = 0;
        foreach (Seat seat in seats.Values)
            if (!seat.disconnected) { remaining = seat; stillHere++; }

        if (stillHere == 0)
        {
            VoidMatch("Both players disconnected - match voided.");
            yield break;
        }

        if (stillHere > 1 || remaining == null) yield break;

        Debug.LogWarning($"[DragonatorWallet] {leaver.name} did not return - " +
                         $"{remaining.name} wins match {matchId} by forfeit.");

        if (escrow != null)
            Message(remaining.connectionId, true, "Your opponent forfeited - paying you the pot.");

        ServerEndMatchByForfeit(remaining, leaver);

        PayWinner(remaining.conn);
    }

    [Server]
    private void ServerEndMatchByForfeit(Seat remaining, Seat leaver)
    {
        GameManager gameManager = FindAnyObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogWarning("[DragonatorWallet] No GameManager to record the forfeit against.");
            return;
        }

        Player winner = remaining?.conn?.identity != null
            ? remaining.conn.identity.GetComponent<Player>()
            : null;

        Player loser = leaver?.conn?.identity != null
            ? leaver.conn.identity.GetComponent<Player>()
            : null;

        gameManager.ServerEndMatch(winner, loser, "forfeit");
    }

    public string ReceiptStake()
    {
        if (escrow == null) return "free";

        foreach (Seat seat in seats.Values)
            if (!string.IsNullOrEmpty(seat.promptAmount)) return seat.promptAmount + " XST";

        return "free";
    }

    [Server]
    private void VoidMatch(string reason)
    {
        if (cancelled) return;

        if (forfeitWatch != null) { StopCoroutine(forfeitWatch); forfeitWatch = null; }
        forfeitDeadline = 0;

        if (escrow == null)
        {
            cancelled = true;
            Debug.LogWarning($"[DragonatorWallet] Match {matchId} voided: {reason}");
            return;
        }

        escrow.Void(matchId, reason);
    }

    [Server]
    public void PayWinner(NetworkConnectionToClient winnerConn)
    {
        if (escrow == null)
        {
            Debug.Log($"[DragonatorWallet] Match {matchId} won, but it was a free match - no pot to pay out.");
            return;
        }

        if (winnerConn == null || !seats.ContainsKey(winnerConn.connectionId))
        {
            Debug.LogError("[DragonatorWallet] PayWinner: unknown winner connection - no payout sent.");
            return;
        }

        if (settleRequested)
        {
            Debug.LogWarning($"[DragonatorWallet] PayWinner called twice for match {matchId} - ignoring.");
            return;
        }
        settleRequested = true;

        escrow.Settle(matchId, winnerConn.connectionId);
    }

    public int ServerPort
    {
        get { return LedgerPort(); }
    }

    public void PromptForPayoutAddress(int connectionId, string amount, int confirmations)
    {
        Seat seat;
        if (!seats.TryGetValue(connectionId, out seat)) return;

        seat.promptPending = true;
        seat.promptAmount = amount;
        seat.promptConfirmations = confirmations;

        FlushPrompts();
    }

    public void ShowDepositAddress(int connectionId, string depositAddress, string amount)
    {
        Seat seat;
        if (!seats.TryGetValue(connectionId, out seat)) return;
        if (!Live(seat)) return;

        TargetShowDepositAddress(seat.conn, depositAddress, amount);
    }

    public void SetPlayerStatus(int connectionId, string status)
    {
        Seat seat;
        if (!seats.TryGetValue(connectionId, out seat)) return;

        string line = $"{seat.name}: {status}";
        if (seat.slot == 1) player1Status = line;
        else player2Status = line;
    }

    public void Message(int connectionId, bool success, string text)
    {
        Seat seat;
        if (!seats.TryGetValue(connectionId, out seat)) return;
        if (!Live(seat)) return;

        TargetFundingMessage(seat.conn, success, text);
    }

    public void SetFundingDeadline(double seconds)
    {
        fundingDeadline = seconds > 0d ? NetworkTime.time + seconds : 0d;
    }

    public void EscrowReady(string matchId)
    {
        escrowReady = true;
        StartCoroutine(WaitForSceneReadyThenBeginMatch());
    }

    public void EscrowVoided(string matchId, string reason)
    {
        cancelled = true;
        fundingDeadline = 0;

        if (forfeitWatch != null) { StopCoroutine(forfeitWatch); forfeitWatch = null; }
        forfeitDeadline = 0;
    }

    public void SettlementSent(int connectionId, string kind, string txid)
    {
        Seat seat;
        if (!seats.TryGetValue(connectionId, out seat)) return;
        if (!Live(seat)) return;

        TargetShowTxid(seat.conn, kind, txid);
    }

    public void Log(string text)
    {
        Debug.Log($"[Escrow] {text}");
    }

    public void Warn(string text)
    {
        Debug.LogWarning($"[Escrow] {text}");
    }

    public void Error(string text)
    {
        Debug.LogError($"[Escrow] {text}");
    }

    private static bool Live(Seat seat)
    {
        return seat.conn != null && NetworkServer.connections.ContainsKey(seat.connectionId);
    }

    [Server]
    private Seat SlotSeat(int slot)
    {
        foreach (Seat seat in seats.Values)
            if (seat.slot == slot) return seat;

        return null;
    }

    [TargetRpc]
    private void TargetPromptPayoutAddress(NetworkConnectionToClient conn, string amount, int confirmations)
    {
        if (BetUI.Instance == null)
        {
            Debug.LogError("[DragonatorWallet] BetUI.Instance is null - cannot prompt for payout address.");
            return;
        }
        BetUI.Instance.ShowPayoutAddressStep(amount, confirmations);
    }

    [TargetRpc]
    private void TargetShowDepositAddress(NetworkConnectionToClient conn, string depositAddress, string amount)
    {
        BetUI.Instance?.ShowDepositStep(depositAddress, amount);
    }

    [TargetRpc]
    private void TargetFundingMessage(NetworkConnectionToClient conn, bool success, string message)
    {
        Debug.Log($"[DragonatorWallet] Funding message: {success} - {message}");
        BetUI.Instance?.ShowFundingMessage(success, message);
    }

    [TargetRpc]
    private void TargetShowTxid(NetworkConnectionToClient conn, string kind, string txid)
    {
        if (kind == DragonatorApi.KindPayout)
            FindFirstObjectByType<OutcomeUI>()?.ShowWinnerTxid(txid);
        else
            BetUI.Instance?.ShowFundingMessage(true, $"Refund sent. TXID: {txid}");
    }

    [ClientRpc]
    private void RpcHideStatusDisplay()
    {
        BetUI.Instance?.HideStatusDisplay();
        BetUI.Instance?.HideBetUI();
    }

    [ClientRpc]
    private void RpcShowStatusDisplay()
    {
        BetUI.Instance?.ShowStatusDisplay();
    }

    [ClientRpc]
    private void RpcFreeMatchNotice()
    {
        BetUI.Instance?.HideBetUI();
    }
}
