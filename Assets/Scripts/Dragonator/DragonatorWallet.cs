using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class DragonatorWallet : NetworkBehaviour
{
    public static DragonatorWallet Instance;

    [Header("Stake")]
    [Tooltip("Stake per player in XST. Parsed as decimal — money must never round-trip through float.")]
    [SerializeField] private string betAmountXst = "0.01";

    [Header("Funding phase")]
    [Tooltip("Confirmations required before a deposit counts. XST blocks are ~5s, so 3 is ~15s.")]
    [SerializeField] private int requiredConfirmations = 3;

    [Tooltip("Seconds players get to fund the match. 120 for testing, raise for production.")]
    [SerializeField] private float fundingTimeoutSeconds = 120f;

    [Tooltip("Seconds between deposit polls.")]
    [SerializeField] private float pollIntervalSeconds = 5f;

    [Tooltip("minconf passed to listreceivedbyaddress. 0 shows unconfirmed payments so the " +
             "UI can display 'Confirming n/3'. Set to 1 if your daemon rejects 0.")]
    [SerializeField] private int pollMinConfirmations = 0;

    [Header("Mid-match disconnect")]
    [Tooltip("Seconds a player who drops mid-match has before they forfeit the pot. Kept " +
             "short because the window cannot yet be used to rejoin — it is only a delay " +
             "until session-token reconnect exists. Set 0 to forfeit immediately.")]
    [SerializeField] private float forfeitGraceSeconds = 5f;

    [Header("Testing")]
    [Tooltip("Starts the match immediately with no stakes collected and no payouts sent. " +
             "Lets the card game be play-tested without an XST daemon. Editor and " +
             "development builds only — forced off in a release build.")]
    [SerializeField] private bool skipFunding = false;

    [Tooltip("Seconds to wait for both clients to load the match scene when skipFunding is on.")]
    [SerializeField] private float skipFundingReadyTimeout = 15f;

    public decimal BetAmount { get; private set; }
    public int RequiredConfirmations => requiredConfirmations;
    public bool SkipFunding => skipFunding;

    [SyncVar(hook = nameof(OnPlayer1StatusChanged))] private string player1Status = "";
    [SyncVar(hook = nameof(OnPlayer2StatusChanged))] private string player2Status = "";

    [SyncVar] private double fundingDeadline;
    public double FundingDeadline => fundingDeadline;

    [SyncVar] private double forfeitDeadline;
    public double ForfeitDeadline => forfeitDeadline;

    private class PlayerBetInfo
    {
        public NetworkConnectionToClient conn;
        public int connectionId;
        public int slot;
        public string name;
        public string payoutAddress;
        public string depositAddress;
        public decimal received;
        public int confirmations;
        public bool sceneReady;
        public bool prompted;
        public bool issuing;
        public bool funded;
        public bool refundStarted;
        public bool disconnected;
        public decimal ledgerRecordedAmount;
        public string lastClientMessage;
    }

    private readonly Dictionary<NetworkConnectionToClient, PlayerBetInfo> betInfos
        = new Dictionary<NetworkConnectionToClient, PlayerBetInfo>();

    private readonly HashSet<NetworkConnectionToClient> earlyReadyConnections
        = new HashSet<NetworkConnectionToClient>();

    private BetLedger ledger;
    private string matchId = "";
    private bool fundingActive;
    private bool cancelled;
    private bool matchStarted;
    private bool payoutStarted;
    private Coroutine fundingLoop;
    private Coroutine forfeitWatch;

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[DragonatorWallet] A second instance appeared — the newest one wins.");
        Instance = this;

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        if (skipFunding)
        {
            skipFunding = false;
            Debug.LogError("[DragonatorWallet] skipFunding is not allowed in a release build — " +
                           "funding has been forced back on.");
        }
#endif
        if (skipFunding)
            Debug.LogWarning("[DragonatorWallet] skipFunding is ON — matches start with no stake " +
                             "and no payout or refund will ever be sent. Never ship this enabled.");

        if (!decimal.TryParse(betAmountXst, NumberStyles.Number, CultureInfo.InvariantCulture,
                              out decimal parsed) || parsed <= 0m)
        {
            parsed = 0.01m;
            Debug.LogError($"[DragonatorWallet] Invalid betAmountXst '{betAmountXst}' — falling back to {parsed}.");
        }

        if (ServerOptions.Configured && BetAmountOption.BetXst > 0m)
        {
            parsed = BetAmountOption.BetXst;
            Debug.Log($"[DragonatorWallet] Using the configured server stake of {parsed} XST.");
        }

        BetAmount = parsed;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        EnsureLedger();
        ReportUnresolvedSends();
        StartCoroutine(RefundOrphanedFundings());
    }

    private void EnsureLedger()
    {
        if (ledger != null) return;
        int port = XSTDragonNetworkManager.singleton != null
            ? XSTDragonNetworkManager.singleton.networkPort
            : 0;
        ledger = BetLedger.ForPort(port);
    }

    private void ReportUnresolvedSends()
    {
        var unresolved = ledger.GetUnresolvedSends();
        foreach (var e in unresolved)
        {
            Debug.LogError($"[DragonatorWallet] UNRESOLVED {e.kind} from a previous run: " +
                           $"{e.amount} XST -> {e.payoutAddress} (match {e.matchId}, record {e.recordId}). " +
                           "Verify against the daemon with listtransactions before resending.");
        }
        if (unresolved.Count > 0)
            Debug.LogError($"[DragonatorWallet] {unresolved.Count} unresolved send(s) need manual review.");
    }

    [Server]
    private IEnumerator RefundOrphanedFundings()
    {
        var orphaned = ledger.GetOrphanedFundings();
        if (orphaned.Count == 0) yield break;

        if (skipFunding)
        {
            Debug.LogError($"[DragonatorWallet] {orphaned.Count} real unsettled stake(s) from a " +
                           "previous run are owed refunds, but skipFunding is on so nothing was sent. " +
                           "They stay in the ledger — restart with skipFunding OFF to settle them.");
            foreach (var f in orphaned)
                Debug.LogError($"[DragonatorWallet] OWED: {f.amount} XST -> {f.payoutAddress} " +
                               $"(match {f.matchId}, conn {f.connectionId}).");
            yield break;
        }

        Debug.LogWarning($"[DragonatorWallet] Found {orphaned.Count} unsettled stake(s) from a " +
                         "previous run — refunding.");

        foreach (var f in orphaned)
        {
            Debug.LogWarning($"[DragonatorWallet] Recovery refund: {f.amount} XST -> {f.payoutAddress} " +
                             $"(match {f.matchId}, conn {f.connectionId}).");

            yield return SendFunds(f.matchId, BetLedger.KindRefund, f.connectionId,
                                   f.payoutAddress, f.amount, null);
        }

        Debug.Log("[DragonatorWallet] Recovery refunds complete.");
    }

    void OnPlayer1StatusChanged(string old, string newVal) =>
        BetUI.Instance?.UpdatePlayerStatus(1, newVal);

    void OnPlayer2StatusChanged(string old, string newVal) =>
        BetUI.Instance?.UpdatePlayerStatus(2, newVal);

    [Server]
    private void SetStatus(PlayerBetInfo info, string state)
    {
        string line = $"{info.name}: {state}";
        if (info.slot == 1) player1Status = line;
        else player2Status = line;
    }

    [Server]
    private void Notify(PlayerBetInfo info, bool success, string message)
    {
        if (info.lastClientMessage == message) return;
        info.lastClientMessage = message;

        if (info.conn == null || !NetworkServer.connections.ContainsKey(info.connectionId)) return;
        TargetFundingMessage(info.conn, success, message);
    }

    [Command(requiresAuthority = false)]
    public void CmdClientReady(NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;

        if (!betInfos.TryGetValue(sender, out var info))
        {
            earlyReadyConnections.Add(sender);
            return;
        }

        info.sceneReady = true;
        Debug.Log($"[DragonatorWallet] Client {sender.connectionId} is scene-ready.");
        PromptReadyPlayers();
    }

    [Server]
    public void InitializeMatch(List<NetworkConnectionToClient> players)
    {
        EnsureLedger();

        betInfos.Clear();
        cancelled = false;
        matchStarted = false;
        payoutStarted = false;

        matchId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        int slot = 1;
        foreach (var conn in players)
        {
            if (conn == null) continue;
            if (slot > 2)
            {
                Debug.LogWarning("[DragonatorWallet] More than 2 connections — ignoring the extras.");
                break;
            }

            string name = conn.identity?.GetComponent<Player>()?.username ?? $"Player {slot}";
            var info = new PlayerBetInfo
            {
                conn = conn,
                connectionId = conn.connectionId,
                slot = slot,
                name = name
            };
            betInfos[conn] = info;
            SetStatus(info, "Waiting for payout address...");
            slot++;
        }

        foreach (var conn in earlyReadyConnections)
            if (betInfos.TryGetValue(conn, out var early)) early.sceneReady = true;
        earlyReadyConnections.Clear();

        if (skipFunding)
        {
            fundingActive = false;
            fundingDeadline = 0;

            Debug.LogWarning($"[DragonatorWallet] Match {matchId}: skipFunding is ON — collecting " +
                             $"no stakes from {betInfos.Count} player(s) and starting immediately.");

            foreach (var info in betInfos.Values)
                SetStatus(info, "<color=#FFAA00>No stake (test mode)</color>");

            StartCoroutine(SkipFundingThenBeginMatch());
            return;
        }

        fundingActive = true;
        fundingDeadline = NetworkTime.time + fundingTimeoutSeconds;

        Debug.Log($"[DragonatorWallet] Match {matchId}: funding open for {fundingTimeoutSeconds}s, " +
                  $"stake {BetAmount} XST per player, {requiredConfirmations} confirmations required.");

        PromptReadyPlayers();

        if (fundingLoop != null) StopCoroutine(fundingLoop);
        fundingLoop = StartCoroutine(FundingLoop());
    }

    [Server]
    private void PromptReadyPlayers()
    {
        if (!fundingActive) return;

        foreach (var info in betInfos.Values)
        {
            if (!info.sceneReady || info.prompted) continue;
            info.prompted = true;
            TargetPromptPayoutAddress(info.conn,
                BetAmount.ToString(CultureInfo.InvariantCulture),
                requiredConfirmations);
        }
    }

    [Server]
    private bool AllSceneReady()
    {
        if (betInfos.Count == 0) return false;
        foreach (var info in betInfos.Values)
            if (!info.sceneReady) return false;
        return true;
    }

    [Server]
    private IEnumerator SkipFundingThenBeginMatch()
    {
        float deadline = Time.realtimeSinceStartup + skipFundingReadyTimeout;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (AllSceneReady() && SlotInfo(1)?.conn?.identity != null) break;
            yield return new WaitForSeconds(0.25f);
        }

        if (!AllSceneReady())
            Debug.LogWarning("[DragonatorWallet] skipFunding: not every client reported scene-ready " +
                             $"within {skipFundingReadyTimeout}s — starting anyway.");

        RpcSkipFundingNotice();
        BeginMatch();
    }

    [Command(requiresAuthority = false)]
    public void CmdSubmitPayoutAddress(string payoutAddress, NetworkConnectionToClient sender = null)
    {
        if (sender == null || !betInfos.TryGetValue(sender, out var info))
        {
            Debug.LogWarning("[DragonatorWallet] Payout address from an unknown connection.");
            return;
        }

        if (!fundingActive)
        {
            Notify(info, false, "The funding window is closed.");
            return;
        }

        if (!string.IsNullOrEmpty(info.depositAddress) || info.issuing)
        {
            Notify(info, false, "Your deposit address has already been issued.");
            return;
        }

        payoutAddress = (payoutAddress ?? "").Trim();
        if (payoutAddress.Length == 0)
        {
            Notify(info, false, "Enter the XST address you want your winnings sent to.");
            return;
        }

        info.issuing = true;
        StartCoroutine(IssueDepositAddress(info, payoutAddress));
    }

    [Server]
    private IEnumerator IssueDepositAddress(PlayerBetInfo info, string payoutAddress)
    {
        JToken validation = null;
        yield return RpcCall("validateaddress", new object[] { payoutAddress },
                             r => validation = RpcResult(r, "validateaddress"));

        if (validation == null)
        {
            info.issuing = false;
            info.lastClientMessage = null;
            Notify(info, false, "Could not reach the wallet daemon. Try again.");
            yield break;
        }

        bool isValid = validation["isvalid"]?.Value<bool>() ?? false;
        if (!isValid)
        {
            info.issuing = false;
            info.lastClientMessage = null;
            Notify(info, false, "That is not a valid XST address. Check it and try again.");
            yield break;
        }

        string depositAddress = null;
        yield return RpcCall("getnewaddress", null, r =>
        {
            JToken result = RpcResult(r, "getnewaddress");
            depositAddress = result?.Type == JTokenType.String ? result.Value<string>() : null;
        });

        if (string.IsNullOrEmpty(depositAddress))
        {
            info.issuing = false;
            info.lastClientMessage = null;
            Notify(info, false, "Could not create a deposit address. Try again.");
            yield break;
        }

        info.payoutAddress = payoutAddress;
        info.depositAddress = depositAddress;
        info.issuing = false;

        ledger.RecordIssue(matchId, info.connectionId, depositAddress, payoutAddress);

        Debug.Log($"[DragonatorWallet] {info.name}: deposit {depositAddress}, payout {payoutAddress}");

        SetStatus(info, "Waiting for payment...");
        TargetShowDepositAddress(info.conn, depositAddress,
                                 BetAmount.ToString(CultureInfo.InvariantCulture));
    }

    [Server]
    private IEnumerator FundingLoop()
    {
        while (fundingActive)
        {
            yield return PollDeposits();
            if (!fundingActive) break;

            if (AllFunded())
            {
                BeginMatch();
                break;
            }

            if (NetworkTime.time >= fundingDeadline)
            {
                Debug.LogWarning($"[DragonatorWallet] Match {matchId}: funding deadline reached.");
                CancelFunding("Funding timed out.");
                break;
            }

            yield return new WaitForSeconds(pollIntervalSeconds);
        }

        fundingLoop = null;
    }

    [Server]
    private IEnumerator PollDeposits()
    {
        bool anyAwaiting = false;
        foreach (var info in betInfos.Values)
            if (!string.IsNullOrEmpty(info.depositAddress) && !info.funded) anyAwaiting = true;

        if (!anyAwaiting) yield break;

        JToken rows = null;
        yield return RpcCall("listreceivedbyaddress",
                             new object[] { pollMinConfirmations, true },
                             r => rows = RpcResult(r, "listreceivedbyaddress"));

        if (rows == null)
        {
            Debug.LogWarning("[DragonatorWallet] Deposit poll failed — retrying next tick.");
            yield break;
        }

        foreach (var info in betInfos.Values)
        {
            if (string.IsNullOrEmpty(info.depositAddress) || info.funded) continue;

            decimal received = 0m;
            int confirmations = 0;

            foreach (var row in rows)
            {
                if (row["address"]?.Value<string>() != info.depositAddress) continue;
                received = row["amount"]?.Value<decimal>() ?? 0m;
                confirmations = row["confirmations"]?.Value<int>() ?? 0;
                break;
            }

            info.received = received;
            info.confirmations = confirmations;

            if (received > 0m && received != info.ledgerRecordedAmount)
            {
                ledger.RecordFunded(matchId, info.connectionId, info.depositAddress,
                                    info.payoutAddress, received);
                info.ledgerRecordedAmount = received;
            }

            if (received <= 0m)
            {
                SetStatus(info, "Waiting for payment...");
                continue;
            }

            if (received < BetAmount)
            {
                SetStatus(info, $"<color=#FFAA00>Underpaid {received}/{BetAmount}</color>");
                Notify(info, false, $"Received {received} XST but the stake is {BetAmount} XST. " +
                                    "Send the difference to the same address.");
                continue;
            }

            if (confirmations < requiredConfirmations)
            {
                SetStatus(info, $"<color=#FFAA00>Confirming {confirmations}/{requiredConfirmations}</color>");
                Notify(info, true, $"Payment seen — waiting for {requiredConfirmations} confirmations " +
                                   $"({confirmations}/{requiredConfirmations}).");
                continue;
            }

            info.funded = true;
            SetStatus(info, "<color=#00FF00>Paid</color>");
            Notify(info, true, $"Payment confirmed ({received} XST). Waiting for your opponent.");
            Debug.Log($"[DragonatorWallet] {info.name} funded: {received} XST at {info.depositAddress}");
        }
    }

    [Server]
    private bool AllFunded()
    {
        if (betInfos.Count < 2) return false;
        foreach (var info in betInfos.Values)
            if (!info.funded) return false;
        return true;
    }

    public bool BothPlayersValidated()
    {
        if (skipFunding) return true;
        if (!NetworkServer.active) return false;
        return AllFunded();
    }

    [Server]
    private void BeginMatch()
    {
        if (matchStarted) return;
        matchStarted = true;
        fundingActive = false;
        fundingDeadline = 0;

        if (skipFunding)
            Debug.LogWarning($"[DragonatorWallet] Match {matchId}: starting unfunded (skipFunding) — " +
                             "nothing written to the ledger.");
        else
        {
            Debug.Log($"[DragonatorWallet] Match {matchId}: both players funded — starting.");
            ledger.RecordMatchState(matchId, BetLedger.MatchPlaying);
        }

        RpcHideStatusDisplay();

        PlayerBetInfo first = SlotInfo(1);
        if (first?.conn?.identity == null)
        {
            Debug.LogError("[DragonatorWallet] Slot 1 has no player identity — cannot start match!");
            return;
        }

        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
            gameManager.StartGameForPlayer(first.conn.identity);
        else
            Debug.LogError("[DragonatorWallet] GameManager not found!");
    }

    [Server]
    public void NotifyPlayerDisconnected(NetworkConnectionToClient conn)
    {
        if (conn == null) return;
        if (!betInfos.TryGetValue(conn, out var info)) return;

        info.disconnected = true;

        if (fundingActive)
        {
            Debug.LogWarning($"[DragonatorWallet] {info.name} left during funding — cancelling match {matchId}.");
            CancelFunding("Your opponent left before the match was funded.");
            return;
        }

        if (!matchStarted || payoutStarted || cancelled) return;

        if (AllDisconnected())
        {
            RefundAllFunded("Both players disconnected — match voided.");
            return;
        }

        Debug.LogWarning($"[DragonatorWallet] {info.name} dropped mid-match — " +
                         $"{forfeitGraceSeconds}s before forfeit.");

        if (forfeitWatch == null)
            forfeitWatch = StartCoroutine(ForfeitCountdown(info));
    }

    [Server]
    private bool AllDisconnected()
    {
        if (betInfos.Count == 0) return false;
        foreach (var info in betInfos.Values)
            if (!info.disconnected) return false;
        return true;
    }

    [Server]
    private IEnumerator ForfeitCountdown(PlayerBetInfo leaver)
    {
        forfeitDeadline = NetworkTime.time + forfeitGraceSeconds;

        SetStatus(leaver, "<color=#FF0000>Disconnected</color>");
        foreach (var info in betInfos.Values)
        {
            if (info.disconnected) continue;
            SetStatus(info, "<color=#FFAA00>Waiting for opponent...</color>");
            Notify(info, false, $"{leaver.name} disconnected. If they do not return you win the pot.");
        }
        RpcShowStatusDisplay();

        while (NetworkTime.time < forfeitDeadline)
        {
            if (AllDisconnected())
            {
                forfeitWatch = null;
                forfeitDeadline = 0;
                RefundAllFunded("Both players disconnected — match voided.");
                yield break;
            }
            yield return null;
        }

        forfeitDeadline = 0;
        forfeitWatch = null;

        PlayerBetInfo remaining = null;
        int stillHere = 0;
        foreach (var info in betInfos.Values)
            if (!info.disconnected) { remaining = info; stillHere++; }

        if (stillHere == 0)
        {
            RefundAllFunded("Both players disconnected — match voided.");
            yield break;
        }

        if (stillHere > 1 || remaining == null) yield break;

        Debug.LogWarning($"[DragonatorWallet] {leaver.name} did not return — " +
                         $"{remaining.name} wins match {matchId} by forfeit.");

        Notify(remaining, true, "Your opponent forfeited — paying you the pot.");
        PayWinner(remaining.conn);
    }

    [Server]
    private void RefundAllFunded(string reason)
    {
        if (cancelled) return;
        cancelled = true;

        if (forfeitWatch != null) { StopCoroutine(forfeitWatch); forfeitWatch = null; }
        forfeitDeadline = 0;

        Debug.LogWarning($"[DragonatorWallet] Match {matchId} voided: {reason}");

        foreach (var info in betInfos.Values)
        {
            if (info.received <= 0m || info.refundStarted) continue;

            info.refundStarted = true;
            SetStatus(info, "<color=#FFAA00>Refunding...</color>");
            Notify(info, false, $"{reason} Refunding {info.received} XST.");

            Debug.Log($"[DragonatorWallet] Voided-match refund: {info.received} XST to " +
                      $"{info.name} ({info.payoutAddress}).");
            StartCoroutine(SendFunds(matchId, BetLedger.KindRefund, info.connectionId,
                                     info.payoutAddress, info.received, info));
        }
    }

    [Server]
    private void CancelFunding(string reason)
    {
        if (cancelled) return;
        cancelled = true;
        fundingActive = false;
        fundingDeadline = 0;

        if (fundingLoop != null) { StopCoroutine(fundingLoop); fundingLoop = null; }

        foreach (var info in betInfos.Values)
        {
            if (info.received > 0m && !info.refundStarted)
            {
                info.refundStarted = true;
                SetStatus(info, "<color=#FFAA00>Refunding...</color>");
                Notify(info, false, $"{reason} Refunding {info.received} XST to {info.payoutAddress}.");

                Debug.Log($"[DragonatorWallet] Refunding {info.received} XST to {info.name} ({info.payoutAddress}).");
                StartCoroutine(SendFunds(matchId, BetLedger.KindRefund, info.connectionId,
                                         info.payoutAddress, info.received, info));
            }
            else
            {
                SetStatus(info, "<color=#FF0000>Cancelled</color>");
                Notify(info, false, reason);
            }
        }
    }

    [Server]
    public void PayWinner(NetworkConnectionToClient winnerConn)
    {
        if (skipFunding)
        {
            Debug.LogWarning($"[DragonatorWallet] Match {matchId} won, but skipFunding was on — " +
                             "there is no pot to pay out.");
            return;
        }

        if (winnerConn == null || !betInfos.TryGetValue(winnerConn, out var winner))
        {
            Debug.LogError("[DragonatorWallet] PayWinner: unknown winner connection — no payout sent.");
            return;
        }

        if (payoutStarted)
        {
            Debug.LogWarning($"[DragonatorWallet] PayWinner called twice for match {matchId} — ignoring.");
            return;
        }
        payoutStarted = true;

        decimal pot = 0m;
        foreach (var info in betInfos.Values) pot += info.received;

        if (pot <= 0m)
        {
            Debug.LogError($"[DragonatorWallet] Match {matchId}: pot is {pot} — nothing to pay out.");
            return;
        }

        decimal fee = ServerOptions.Configured ? HostFeeOption.FeeXst : 0m;
        if (fee < 0m) fee = 0m;

        if (fee >= pot)
        {
            Debug.LogError($"[DragonatorWallet] Match {matchId}: host fee {fee} XST is not less than the " +
                           $"pot {pot} XST — paying the full pot instead of shorting the winner.");
            fee = 0m;
        }

        decimal payout = pot - fee;

        if (fee > 0m)
            Debug.Log($"[DragonatorWallet] Match {matchId}: pot {pot} XST, host fee {fee} XST retained.");

        Debug.Log($"[DragonatorWallet] Match {matchId}: paying {payout} XST to {winner.name} ({winner.payoutAddress}).");
        StartCoroutine(SendFunds(matchId, BetLedger.KindPayout, winner.connectionId,
                                 winner.payoutAddress, payout, winner));
    }

    [Server]
    private IEnumerator SendFunds(string sendMatchId, string kind, int connectionId,
                                  string address, decimal amount, PlayerBetInfo notify)
    {
        if (skipFunding)
        {
            Debug.LogWarning($"[DragonatorWallet] {kind} of {amount} XST to {address} suppressed " +
                             "because skipFunding is on.");
            yield break;
        }

        if (string.IsNullOrEmpty(address) || amount <= 0m)
        {
            Debug.LogError($"[DragonatorWallet] {kind} skipped for match {sendMatchId}: " +
                           $"address='{address}', amount={amount}. MANUAL REVIEW REQUIRED.");
            yield break;
        }

        EnsureLedger();

        if (ledger.HasSettled(sendMatchId, kind, connectionId))
        {
            Debug.LogWarning($"[DragonatorWallet] {kind} for match {sendMatchId} / conn {connectionId} " +
                             "already settled — not sending again.");
            yield break;
        }

        SendToAddress sender = FindFirstObjectByType<SendToAddress>();
        if (sender == null)
        {
            Debug.LogError("[DragonatorWallet] SendToAddress component not found — cannot send funds!");
            yield break;
        }

        string recordId = ledger.BeginSend(sendMatchId, kind, connectionId, address, amount);

        string txid = null;
        bool done = false;

        Task.Run(async () =>
        {
            try { txid = await sender.SendTransaction(address, amount); }
            catch (Exception e) { txid = "Error: " + e.Message; }
            finally { done = true; }
        });

        while (!done) yield return null;

        bool ok = !string.IsNullOrEmpty(txid)
               && !txid.StartsWith("Error")
               && !txid.StartsWith("Parsing error")
               && !txid.StartsWith("Unexpected")
               && !txid.StartsWith("Transaction failed");

        if (ok)
        {
            ledger.CompleteSend(recordId, txid);
            Debug.Log($"[DragonatorWallet] {kind} sent: {amount} XST -> {address}, txid {txid}");

            if (kind == BetLedger.KindPayout)
                ledger.RecordMatchState(sendMatchId, BetLedger.MatchSettled);

            if (notify?.conn != null && NetworkServer.connections.ContainsKey(connectionId))
                TargetShowTxid(notify.conn, kind, txid);
        }
        else
        {
            ledger.FailSend(recordId, txid ?? "null response");
            Debug.LogError($"[DragonatorWallet] {kind} FAILED: {amount} XST -> {address} ({txid}). " +
                           $"Ledger record {recordId} needs manual review.");

            if (notify != null)
            {
                notify.lastClientMessage = null;
                Notify(notify, false, $"{kind} could not be sent. Keep this reference: {recordId}");
            }
        }
    }

    private IEnumerator RpcCall(string method, object[] args, Action<string> onResult)
    {
        RpcHandler rpc = RpcHandler.GetInstance();
        string response = null;
        bool done = false;

        Task.Run(async () =>
        {
            try { response = await rpc.SendRpcRequest(method, args); }
            catch (Exception e) { Debug.LogError($"[DragonatorWallet] {method} threw: {e.Message}"); }
            finally { done = true; }
        });

        while (!done) yield return null;
        onResult(response);
    }

    private static JToken RpcResult(string response, string method)
    {
        if (string.IsNullOrEmpty(response)) return null;
        try
        {
            JObject parsed = JObject.Parse(response);
            JToken error = parsed["error"];
            if (error != null && error.Type != JTokenType.Null)
            {
                Debug.LogError($"[DragonatorWallet] {method} returned error: {error}");
                return null;
            }
            return parsed["result"];
        }
        catch (Exception e)
        {
            Debug.LogError($"[DragonatorWallet] {method} response unparseable: {e.Message}");
            return null;
        }
    }

    [Server]
    private PlayerBetInfo SlotInfo(int slot)
    {
        foreach (var info in betInfos.Values)
            if (info.slot == slot) return info;
        return null;
    }

    [TargetRpc]
    private void TargetPromptPayoutAddress(NetworkConnectionToClient conn, string amount, int confirmations)
    {
        if (BetUI.Instance == null)
        {
            Debug.LogError("[DragonatorWallet] BetUI.Instance is null — cannot prompt for payout address.");
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
        Debug.Log($"[DragonatorWallet] Funding message: {success} — {message}");
        BetUI.Instance?.ShowFundingMessage(success, message);
    }

    [TargetRpc]
    private void TargetShowTxid(NetworkConnectionToClient conn, string kind, string txid)
    {
        if (kind == BetLedger.KindPayout)
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
    private void RpcSkipFundingNotice()
    {
        Debug.LogWarning("[DragonatorWallet] Test mode — no stake was taken and no winnings will be paid.");
        BetUI.Instance?.HideBetUI();
    }
}
