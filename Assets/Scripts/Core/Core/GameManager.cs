using System;
using UnityEngine;
using UnityEngine.UI;
using Mirror;
using TMPro;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class GameManager : NetworkBehaviour
{
    [Header("Health")]
    public int maxHealth = 30;

    [Header("Mana")]
    public int maxMana = 10;

    [Header("Hand")]
    public int handSize = 7;
    public PlayerHand playerHand;
    public PlayerHand enemyHand;

    [Header("Deck")]
    public int deckSize = 30;
    public int identicalCardCount = 2;

    [Header("Battlefield")]
    public Battlefield playerField;
    public Battlefield enemyField;

    [Header("Turn Management")]
    public GameObject endTurnButton;
    [Tooltip("The turn button itself. Left empty, the first Button under endTurnButton is used.")]
    public RectTransform turnButtonRect;
    public Vector2 turnButtonMyTurnPosition = new Vector2(727f, -58f);
    public Vector2 turnButtonOpponentPosition = new Vector2(727f, 58f);
    public Color turnButtonWaitingColor = Color.black;
    public float turnButtonMoveDuration = 0.35f;
    [HideInInspector] public bool isOurTurn = false;

    private bool turnButtonCaptured;
    private Button turnButtonButton;
    private Image turnButtonImage;
    private Color turnButtonActiveColor = Color.white;
    [SyncVar, HideInInspector] public int turnCount = 1;

    [Tooltip("Seconds the server waits for players to commit their shuffle seeds, and again for them to reveal.")]
    public float shuffleSeedSeconds = 8f;

    [SyncVar, HideInInspector] public string shuffleCommitment = "";
    [SyncVar, HideInInspector] public string shuffleSeedCommitments = "";
    [SyncVar, HideInInspector] public bool shuffleSealed = false;
    [SyncVar, HideInInspector] public string shuffleContributions = "";
    [SyncVar, HideInInspector] public string shuffleDeals = "";
    [SyncVar(hook = nameof(OnShuffleRevealed)), HideInInspector] public string shuffleReveal = "";
    [SyncVar(hook = nameof(OnReceiptSignatures)), HideInInspector] public string matchReceiptSignatures = "";
    [SyncVar(hook = nameof(OnMatchReceipt)), HideInInspector] public string matchReceipt = "";

    private readonly Dictionary<string, string> serverSignatures = new Dictionary<string, string>();

    private readonly Dictionary<int, string> seatKeyByConnection = new Dictionary<int, string>();
    private long matchStartedUnix;
    private bool matchEnded;
    private bool witnessed;

    public override void OnStartServer()
    {
        base.OnStartServer();

        MatchFairness.Clear();
        MatchFairness.Begin();
        shuffleCommitment = MatchFairness.CommitmentHex;
        shuffleSeedCommitments = "";
        shuffleSealed = false;
        shuffleContributions = "";
        shuffleDeals = "";
        shuffleReveal = "";
        matchReceipt = "";
        matchReceiptSignatures = "";
        serverSignatures.Clear();
        matchEnded = false;
        witnessed = false;
    }

    [Server]
    public void ServerRevealShuffle()
    {
        if (!MatchFairness.Settled) return;
        if (!string.IsNullOrEmpty(shuffleReveal)) return;

        ServerPublishDealtOrders();
        shuffleReveal = MatchFairness.RevealHex;
        Debug.Log("GameManager: revealed the shuffle seed " + shuffleReveal);
    }

    [Server]
    public void ServerPublishDealtOrders()
    {
        shuffleDeals = MatchFairness.DealtOrdersText;
    }

    private void OnShuffleRevealed(string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(newValue)) return;

        if (ReplayMatch.Active)
        {
            LocalShuffleProof.Watching();
            RefreshOutcomeUI();
            return;
        }

        LocalShuffleProof.Published published = new LocalShuffleProof.Published
        {
            commitment = shuffleCommitment,
            seedCommitments = shuffleSeedCommitments,
            contributions = shuffleContributions,
            deals = shuffleDeals,
            reveal = newValue
        };

        LocalShuffleProof.Verify(published, ClientCollectOtherDeals());
        RefreshOutcomeUI();
    }

    private List<LocalShuffleProof.PlayerDeal> ClientCollectOtherDeals()
    {
        List<LocalShuffleProof.PlayerDeal> others = new List<LocalShuffleProof.PlayerDeal>();

        foreach (Deck deck in FindObjectsByType<Deck>(FindObjectsSortMode.None))
        {
            if (deck == null || deck.isLocalPlayer) continue;

            others.Add(new LocalShuffleProof.PlayerDeal
            {
                netId = deck.netIdentity.netId,
                username = deck.player != null ? deck.player.username : "",
                composition = deck.StartingComposition()
            });
        }

        return others;
    }

    [Header("Turn Timer")]
    [Tooltip("Seconds a player gets per turn before the turn is passed automatically.")]
    public float turnSeconds = 60f;
    [Tooltip("Seconds remaining at which the countdown turns to the warning colour.")]
    public float turnWarningSeconds = 20f;
    public TMP_Text turnTimerText;
    public Color turnTimerColor = Color.white;
    public Color turnTimerWarningColor = new Color(1f, 0.16f, 0.16f);

    [SyncVar, HideInInspector] public double turnDeadline;

    [SyncVar(hook = nameof(OnTurnOwnerChanged)), HideInInspector] public uint currentTurnNetId;

    [SyncVar, HideInInspector] public bool practiceMode = false;

    [Server]
    public bool IsTurnOf(Player player) =>
        player != null && currentTurnNetId != 0 && player.netId == currentTurnNetId;

    [Server]
    public bool IsTurnOf(NetworkConnectionToClient conn) =>
        conn?.identity != null && IsTurnOf(conn.identity.GetComponent<Player>());

    [HideInInspector] public bool isHoveringField = false;

    public SeatList players = new SeatList();
    public List<GameOutcome> gameOutcomes = new List<GameOutcome>();

    public struct GameOutcome
    {
        public string username;
        public bool isWinner;
        public uint netId;
    }

    [Server]
    public void RecordGameOutcome(Player player, bool isWinner)
    {
        GameOutcome outcome = new GameOutcome
        {
            username = player.username,
            isWinner = isWinner,
            netId = player.netIdentity.netId
        };
        gameOutcomes.Add(outcome);
        Debug.Log($"GameManager: Recorded outcome - {player.username} (netId: {outcome.netId}) is {(isWinner ? "Winner" : "Loser")}");
        RpcSyncGameOutcomes(gameOutcomes);
    }

    [ClientRpc]
    private void RpcSyncGameOutcomes(List<GameOutcome> outcomes)
    {
        gameOutcomes = outcomes;
        Debug.Log($"GameManager: Synced {gameOutcomes.Count} game outcomes on client.");
        OutcomeUI outcomeUI = FindAnyObjectByType<OutcomeUI>();
        if (outcomeUI != null)
        {
            outcomeUI.UpdateOutcomeDisplay();
        }
    }

    [Server]
    public void ServerEndMatch(Player winner, Player loser, string reason)
    {
        if (matchEnded)
        {
            Debug.LogWarning("GameManager: the match has already ended - ignoring.");
            return;
        }
        matchEnded = true;

        if (loser != null) RecordGameOutcome(loser, false);
        if (winner != null) RecordGameOutcome(winner, true);

        ServerRevealShuffle();

        try
        {
            ServerPublishReceipt(winner, reason);
        }
        catch (Exception e)
        {
            Debug.LogError($"GameManager: the match receipt could not be built ({e.GetType().Name}: {e.Message}). The match result and any payout are unaffected.");
        }

        ServerStopTheClock();
    }

    [Server]
    private void ServerStopTheClock()
    {
        currentTurnNetId = 0;
        turnDeadline = 0d;

        Debug.Log("GameManager: the match is over, so the turn clock is stopped and no further turn begins.");
    }

    [Server]
    private void ServerPublishReceipt(Player winner, string reason)
    {
        if (MatchFairness.Replaying)
        {
            Debug.Log("GameManager: this was a replay, so no receipt and no replay file are written.");
            return;
        }

        MatchReceipt receipt = new MatchReceipt
        {
            server = practiceMode ? "practice" : ServerBanner.OnionAddress(),
            match = shuffleCommitment,
            started = matchStartedUnix,
            ended = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            seed = shuffleReveal,
            mix = shuffleContributions,
            result = winner != null ? winner.publicKey : "draw",
            reason = reason,
            stake = practiceMode ? "free" : ServerStakeDescription()
        };

        seatKeyByConnection.Clear();

        foreach (Player entry in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (entry == null || string.IsNullOrEmpty(entry.publicKey)) continue;

            NetworkConnectionToClient seatConnection =
                entry.netIdentity != null ? entry.netIdentity.connectionToClient : null;

            if (seatConnection != null)
                seatKeyByConnection[seatConnection.connectionId] = entry.publicKey;

            receipt.seats.Add(new MatchReceipt.Seat
            {
                netId = entry.netId,
                bot = seatConnection == null,
                publicKeyHex = entry.publicKey,
                username = entry.username
            });
        }

        if (receipt.seats.Count == 0)
        {
            Debug.LogWarning("GameManager: no player published an identity key, so this match gets no receipt.");
            return;
        }

        receipt.replay = ServerWriteReplay(receipt, reason);

        serverSignatures.Clear();
        matchReceiptSignatures = "";
        matchReceipt = receipt.Canonical();

        Debug.Log($"GameManager: published the match receipt, digest {receipt.DigestHex()}, {receipt.seats.Count} seat(s).");

        ServerSignSeatsWeHoldKeysFor(receipt);
        ServerAskBotsToSign(receipt);
    }

    [HideInInspector] public MatchReplay replay;

    [Server]
    private MatchReplay ServerReplay()
    {
        if (replay == null) replay = new MatchReplay();
        return replay;
    }

    [Server]
    public void ReplayRecordTurn(uint playerNetId)
    {
        MatchReplay log = ServerReplay();

        log.RecordTurn(turnCount, playerNetId);
        log.RecordCheck(turnCount, ServerBoardState());
    }

    [Server]
    public void ReplayRecordPlay(Player owner, int handIndex, string cardId, uint cardNetId)
    {
        if (owner == null) return;

        ServerReplay().RecordPlay(owner.netId, handIndex, cardId, cardNetId);
    }

    [Server]
    public void ReplayRecordAttack(Player owner, uint attackerNetId, uint targetNetId, bool targetIsPlayer)
    {
        if (owner == null) return;

        ServerReplay().RecordAttack(owner.netId, attackerNetId, targetNetId, targetIsPlayer);
    }

    [Server]
    public void ReplayRecordEnd(Player owner)
    {
        if (owner == null) return;

        ServerReplay().RecordEnd(owner.netId);
    }

    [Server]
    private string ServerBoardState()
    {
        MatchReplay log = ServerReplay();
        List<string> parts = new List<string>();

        foreach (Player entry in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (entry == null) continue;

            StringBuilder sb = new StringBuilder();

            sb.Append(log.SeatOf(entry.netId)).Append('/')
              .Append(entry.health).Append('/')
              .Append(entry.mana).Append('/')
              .Append(entry.deck != null ? entry.deck.hand.Count : 0);

            List<string> field = new List<string>();

            foreach (BoardCard card in FindObjectsByType<BoardCard>(FindObjectsSortMode.None))
            {
                if (card == null || card.owner != entry) continue;
                if (card.health <= 0) continue;

                field.Add(log.StableOf(card.netId) + "=" + card.strength + "/" + card.health +
                          "/" + card.waitTurn + (card.hasAttackedThisTurn ? "a" : ""));
            }

            field.Sort(StringComparer.Ordinal);
            sb.Append('[').Append(string.Join(",", field.ToArray())).Append(']');

            parts.Add(sb.ToString());
        }

        parts.Sort(StringComparer.Ordinal);

        return string.Join("|", parts.ToArray());
    }

    [Server]
    private string ServerWriteReplay(MatchReceipt receipt, string reason)
    {
        MatchReplay log = replay;

        if (log == null || log.Moves == 0)
        {
            Debug.Log("GameManager: no moves were recorded, so this match gets no replay.");
            return "";
        }

        try
        {
            log.match = shuffleCommitment;
            log.seed = shuffleReveal;
            log.mix = shuffleContributions;
            log.result = receipt.result;
            log.reason = reason;

            log.seats.Clear();

            foreach (MatchReceipt.Seat seat in receipt.seats)
                log.seats.Add(new MatchReplay.Seat
                {
                    index = log.SeatOf(seat.netId),
                    netId = seat.netId,
                    username = seat.username,
                    publicKeyHex = seat.publicKeyHex
                });

            log.seats.Sort(delegate (MatchReplay.Seat left, MatchReplay.Seat right)
            {
                return left.index.CompareTo(right.index);
            });

            log.Seal();

            string digest = log.DigestHex();

            if (!MatchReplayStore.Save(digest, log.Canonical())) return "";

            Debug.Log($"GameManager: wrote the match replay {digest}, {log.Moves} move(s).");

            return digest;
        }
        catch (Exception e)
        {
            Debug.LogError($"GameManager: the match replay could not be written ({e.GetType().Name}: {e.Message}). " +
                           "The match result and any payout are unaffected.");
            return "";
        }
    }

    [Server]
    private void ServerAskBotsToSign(MatchReceipt receipt)
    {
        RemoteBrain[] brains = FindObjectsByType<RemoteBrain>(FindObjectsSortMode.None);
        if (brains.Length == 0) return;

        string digest = receipt.DigestHex();

        foreach (RemoteBrain brain in brains)
        {
            if (brain == null || string.IsNullOrEmpty(brain.BotKey)) continue;
            if (serverSignatures.ContainsKey(brain.BotKey)) continue;

            brain.ServerRequestReceiptSignature(digest);
        }
    }

    [Server]
    public void ServerAcceptBotSignature(string publicKeyHex, string signatureHex)
    {
        if (string.IsNullOrEmpty(matchReceipt) || string.IsNullOrEmpty(publicKeyHex)) return;

        MatchReceipt receipt = MatchReceipt.Parse(matchReceipt);
        if (receipt == null) return;

        bool seated = false;
        foreach (MatchReceipt.Seat seat in receipt.seats)
            if (seat.publicKeyHex == publicKeyHex) seated = true;

        if (!seated)
        {
            Debug.LogWarning($"GameManager: {Short(publicKeyHex)} offered a receipt signature but holds no seat - ignored.");
            return;
        }

        if (!PlayerIdentity.Verify(publicKeyHex, receipt.Digest(), signatureHex))
        {
            Debug.LogWarning($"GameManager: the bot {Short(publicKeyHex)} sent a receipt signature that does not verify - ignored.");
            return;
        }

        ServerRecordSignature(publicKeyHex, signatureHex);
        Debug.Log($"GameManager: the bot {Short(publicKeyHex)} signed the match receipt.");
    }

    [Server]
    private void ServerSignSeatsWeHoldKeysFor(MatchReceipt receipt)
    {
        bool anyBot = false;
        foreach (MatchReceipt.Seat seat in receipt.seats)
            if (seat.bot) anyBot = true;

        if (!anyBot) return;

        PlayerIdentity bot = PracticeMode.BotIdentity;
        if (bot == null) return;

        foreach (MatchReceipt.Seat seat in receipt.seats)
        {
            if (!seat.bot || seat.publicKeyHex != bot.PublicKeyHex) continue;

            ServerRecordSignature(seat.publicKeyHex, bot.SignHex(receipt.Digest()));
            Debug.Log("GameManager: signed the bot's seat with the key this server holds.");
        }
    }

    [Server]
    private string ServerStakeDescription()
    {
        DragonatorWallet wallet = FindAnyObjectByType<DragonatorWallet>();

        return wallet != null ? wallet.ReceiptStake() : "free";
    }

    [Command(requiresAuthority = false)]
    public void CmdSignReceipt(string signatureHex, NetworkConnectionToClient sender = null)
    {
        if (string.IsNullOrEmpty(matchReceipt)) return;

        string publicKey = null;
        if (sender != null) seatKeyByConnection.TryGetValue(sender.connectionId, out publicKey);

        if (string.IsNullOrEmpty(publicKey))
        {
            Player live = sender?.identity != null ? sender.identity.GetComponent<Player>() : null;
            if (live != null) publicKey = live.publicKey;
        }

        if (string.IsNullOrEmpty(publicKey))
        {
            Debug.LogWarning("GameManager: a receipt signature arrived from a connection that held no seat - ignored.");
            return;
        }

        MatchReceipt receipt = MatchReceipt.Parse(matchReceipt);
        if (receipt == null) return;

        if (!PlayerIdentity.Verify(publicKey, receipt.Digest(), signatureHex))
        {
            Debug.LogWarning($"GameManager: {Short(publicKey)} sent a receipt signature that does not verify - ignored.");
            return;
        }

        ServerRecordSignature(publicKey, signatureHex);
        Debug.Log($"GameManager: {Short(publicKey)} signed the match receipt.");
    }

    private static string Short(string hex)
    {
        return string.IsNullOrEmpty(hex) || hex.Length <= 16 ? hex : hex.Substring(0, 16);
    }

    [Server]
    private void ServerRecordSignature(string publicKeyHex, string signatureHex)
    {
        serverSignatures[publicKeyHex] = signatureHex;

        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<string, string> entry in serverSignatures)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(entry.Key).Append(':').Append(entry.Value);
        }

        matchReceiptSignatures = sb.ToString();

        ServerOfferToWitness();
    }

    [Server]
    private void ServerOfferToWitness()
    {
        if (witnessed || string.IsNullOrEmpty(matchReceipt)) return;
        if (!MatchWitness.Installed) return;
        if (!ReceiptFullySigned()) return;

        witnessed = true;
        MatchWitness.Record(matchReceipt, matchReceiptSignatures, true);
    }

    private void OnReceiptSignatures(string oldValue, string newValue)
    {
        RefreshOutcomeUI();
    }

    private void RefreshOutcomeUI()
    {
        OutcomeUI outcomeUI = FindAnyObjectByType<OutcomeUI>();
        if (outcomeUI != null) outcomeUI.ShowFairness();
    }

    private void OnMatchReceipt(string oldValue, string newValue)
    {
        RefreshOutcomeUI();

        if (string.IsNullOrEmpty(newValue) || !isClient) return;

        MatchReceipt receipt = MatchReceipt.Parse(newValue);
        if (receipt == null)
        {
            Debug.LogWarning("GameManager: the match receipt could not be read on this client.");
            return;
        }

        string mine = PlayerIdentity.Mine.PublicKeyHex;

        foreach (MatchReceipt.Seat seat in receipt.seats)
        {
            if (seat.publicKeyHex != mine) continue;

            if (!LocalShuffleProof.Checked)
            {
                Debug.LogError("GameManager: refusing to sign the match receipt - this client never checked the match.");
                return;
            }

            if (!LocalShuffleProof.Passed)
            {
                Debug.LogError($"GameManager: REFUSING to sign the match receipt - {LocalShuffleProof.Result}");
                return;
            }

            CmdSignReceipt(PlayerIdentity.Mine.SignHex(receipt.Digest()));
            Debug.Log($"GameManager: signed the match receipt {receipt.DigestHex()}.");
            return;
        }
    }

    public bool ReceiptFullySigned()
    {
        MatchReceipt receipt = MatchReceipt.Parse(matchReceipt);
        if (receipt == null) return false;

        Dictionary<string, string> signatures = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(matchReceiptSignatures))
        {
            foreach (string part in matchReceiptSignatures.Split(';'))
            {
                string[] bits = part.Split(':');
                if (bits.Length == 2) signatures[bits[0]] = bits[1];
            }
        }

        return receipt.FullySigned(signatures);
    }

    [Server]
    public void ShowDisconnectMessageOnClients(string message)
    {
        RpcShowDisconnectMessage(message);
        Debug.Log($"GameManager: Called RpcShowDisconnectMessage with '{message}'");
    }

    [ClientRpc]
    private void RpcShowDisconnectMessage(string message)
    {
        DisconnectUI disconnectUI = FindAnyObjectByType<DisconnectUI>();
        if (disconnectUI != null)
        {
            disconnectUI.ShowDisconnectMessage(message);
            Debug.Log($"Client: Displaying '{message}' message.");
        }
        else
        {
            Debug.LogWarning("Client: DisconnectUI not found, cannot display message.");
        }
    }

    [Server]
    public void StartGameForPlayer(NetworkIdentity firstPlayerIdentity)
    {
        if (practiceMode)
        {
            Debug.LogWarning("GameManager: starting a PRACTICE match - no stake, no payout.");
        }
        else
        {
            DragonatorWallet wallet = FindAnyObjectByType<DragonatorWallet>();
            if (wallet == null || !wallet.BothPlayersValidated())
            {
                Debug.LogWarning("GameManager: Cannot start - bets not validated yet.");
                return;
            }
        }

        if (currentTurnNetId != 0)
        {
            Debug.LogWarning("GameManager: StartGameForPlayer called twice - ignoring.");
            return;
        }

        Debug.Log("GameManager: StartGameForPlayer called on server.");
        turnCount = 1;

        Player first = firstPlayerIdentity != null ? firstPlayerIdentity.GetComponent<Player>() : null;
        if (first == null)
        {
            Debug.LogError("GameManager: first player identity has no Player component.");
            return;
        }

        if (dealStarted)
        {
            Debug.LogWarning("GameManager: the match is already under way - ignoring.");
            return;
        }
        dealStarted = true;

        StartCoroutine(ServerSealSeedsThenDeal(firstPlayerIdentity, first));
    }

    private bool dealStarted;

    [Server]
    private int ServerExpectedContributors()
    {
        int expected = 0;

        foreach (Player entry in FindObjectsByType<Player>(FindObjectsSortMode.None))
            if (entry.netIdentity != null && entry.netIdentity.connectionToClient != null) expected++;

        return expected;
    }

    [Server]
    private IEnumerator ServerSealSeedsThenDeal(NetworkIdentity firstPlayerIdentity, Player first)
    {
        int expected = MatchFairness.Replaying ? 0 : ServerExpectedContributors();
        float deadline = Time.realtimeSinceStartup + shuffleSeedSeconds;

        while (Time.realtimeSinceStartup < deadline && MatchFairness.Committed < expected)
            yield return null;

        if (MatchFairness.Committed < expected)
            Debug.LogWarning($"GameManager: only {MatchFairness.Committed} of {expected} player(s) committed a seed in time - sealing anyway.");

        MatchFairness.SealSeeds();
        shuffleSeedCommitments = MatchFairness.SeedCommitmentsHex;
        shuffleSealed = true;

        deadline = Time.realtimeSinceStartup + shuffleSeedSeconds;

        while (Time.realtimeSinceStartup < deadline && !MatchFairness.AllRevealed)
            yield return null;

        if (!MatchFairness.AllRevealed)
            Debug.LogWarning("GameManager: not every committed seed was revealed in time - dealing without the missing ones.");

        ServerDealEveryHand();
        ServerBeginTurnFor(first);
        RpcStartGame(firstPlayerIdentity);
    }

    [Server]
    private void ServerDealEveryHand()
    {
        MatchFairness.Settle();
        matchStartedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        shuffleContributions = MatchFairness.ContributionsHex;

        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);

        foreach (Player entry in players)
            if (entry.deck != null) entry.deck.ServerShuffleAndDeal();

        Debug.Log($"GameManager: dealt {players.Length} opening hand(s) from the settled shuffle.");
    }

    [Server]
    private void ServerBeginTurnFor(Player player)
    {
        currentTurnNetId = player.netId;

        ReplayRecordTurn(player.netId);

        if (player.mana < player.maxMana)
        {
            player.currentMax++;
            player.mana = player.currentMax;
        }

        foreach (BoardCard card in FindObjectsByType<BoardCard>(FindObjectsSortMode.None))
            if (card.owner == player) card.ServerBeginTurn();

        if (player.deck != null) player.deck.ServerDrawCards(1);

        turnDeadline = turnSeconds > 0f ? NetworkTime.time + turnSeconds : 0d;

        Debug.Log($"GameManager: Turn {turnCount} begins for {player.username} (mana {player.mana}).");
    }

    void Update()
    {
        if (isServer) ServerCheckTurnTimeout();
        UpdateTurnTimerUI();
    }

    [Server]
    private void ServerCheckTurnTimeout()
    {
        if (currentTurnNetId == 0 || turnDeadline <= 0d) return;
        if (NetworkTime.time < turnDeadline) return;

        Player current = ServerFindPlayerByNetId(currentTurnNetId);

        turnDeadline = 0d;

        if (current == null)
        {
            Debug.LogWarning("GameManager: turn timer expired but the turn holder is gone.");
            return;
        }

        Debug.Log($"GameManager: {current.username} ran out of time after {turnSeconds}s - passing the turn.");
        ServerEndTurn(current);
    }

    [Server]
    private Player ServerFindPlayerByNetId(uint netId)
    {
        if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity)) return null;
        return identity != null ? identity.GetComponent<Player>() : null;
    }

    private void UpdateTurnTimerUI()
    {
        if (turnTimerText == null) return;

        if (currentTurnNetId == 0 || turnDeadline <= 0d)
        {
            turnTimerText.text = "";
            return;
        }

        double remaining = turnDeadline - NetworkTime.time;
        if (remaining < 0d) remaining = 0d;

        turnTimerText.text = Mathf.CeilToInt((float)remaining).ToString();
        turnTimerText.color = remaining <= turnWarningSeconds ? turnTimerWarningColor : turnTimerColor;
    }

    [Server]
    private Player ServerFindOpponentOf(Player player)
    {
        foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
            if (p != player && p.health > 0) return p;
        return null;
    }

    [ClientRpc]
    private void RpcStartGame(NetworkIdentity firstPlayerIdentity)
    {
        Debug.Log("GameManager: RpcStartGame called on client.");

        if (Player.localPlayer == null)
        {
            Debug.LogError("GameManager: Player.localPlayer is null, cannot start game.");
            return;
        }

        if (endTurnButton == null)
        {
            Debug.LogError("GameManager: endTurnButton is not assigned in the Inspector.");
            return;
        }

        RefreshTurnUI();
        Debug.Log($"GameManager: Game started for {Player.localPlayer.username}, isOurTurn: {isOurTurn}, mana: {Player.localPlayer.mana}");
    }

    private void RefreshTurnUI()
    {
        if (Player.localPlayer == null) return;

        isOurTurn = Player.localPlayer.netId == currentTurnNetId;
        if (endTurnButton == null) return;

        CaptureTurnButton();
        endTurnButton.SetActive(true);
        MoveTurnButton(isOurTurn);
    }

    private void CaptureTurnButton()
    {
        if (turnButtonCaptured || endTurnButton == null) return;
        turnButtonCaptured = true;

        if (turnButtonRect == null)
        {
            Button found = endTurnButton.GetComponentInChildren<Button>(true);
            if (found != null) turnButtonRect = found.GetComponent<RectTransform>();
        }

        if (turnButtonRect == null) return;

        turnButtonButton = turnButtonRect.GetComponent<Button>();
        turnButtonImage = turnButtonRect.GetComponent<Image>();
        if (turnButtonImage != null) turnButtonActiveColor = turnButtonImage.color;
    }

    private void MoveTurnButton(bool ourTurn)
    {
        if (turnButtonRect == null) return;

        Vector2 target = ourTurn ? turnButtonMyTurnPosition : turnButtonOpponentPosition;

        DOTween.Kill(turnButtonRect);
        turnButtonRect.DOAnchorPos(target, turnButtonMoveDuration).SetEase(Ease.OutCubic).SetLink(turnButtonRect.gameObject);

        if (turnButtonImage != null)
        {
            Color colour = ourTurn ? turnButtonActiveColor : turnButtonWaitingColor;
            colour.a = turnButtonActiveColor.a;

            DOTween.Kill(turnButtonImage);
            turnButtonImage.DOColor(colour, turnButtonMoveDuration).SetLink(turnButtonImage.gameObject);
        }

        if (turnButtonButton != null) turnButtonButton.interactable = ourTurn;
    }

    [Command(requiresAuthority = false)]
    public void CmdSetHandHover(int index, NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;

        NetworkConnectionToClient opponent = ServerFindOpponentConnection(sender);
        if (opponent == null) return;

        TargetSetEnemyHandHover(opponent, index);
    }

    [Server]
    private NetworkConnectionToClient ServerFindOpponentConnection(NetworkConnectionToClient sender)
    {
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            if (conn != sender && conn.identity != null) return conn;

        return null;
    }

    [TargetRpc]
    private void TargetSetEnemyHandHover(NetworkConnectionToClient target, int index)
    {
        if (enemyHand != null) enemyHand.SetHoveredCard(index);
    }

    [Command(requiresAuthority = false)]
    public void CmdOnFieldCardHover(GameObject cardObject, bool activateShine, bool targeting)
    {
        if (cardObject == null)
        {
            Debug.LogWarning("GameManager: CmdOnFieldCardHover rejected - null card object.");
            return;
        }

        RpcFieldCardHover(cardObject, activateShine, targeting);
    }

    [ClientRpc]
    public void RpcFieldCardHover(GameObject cardObject, bool activateShine, bool targeting)
    {
        if (isHoveringField) return;
        if (cardObject == null) return;

        BoardCard card = cardObject.GetComponent<BoardCard>();
        if (card == null || card.shine == null) return;

        Color shine = activateShine ? card.hoverColor : Color.clear;
        card.shine.color = targeting ? card.targetColor : shine;
    }

    [Command(requiresAuthority = false)]
    public void CmdAimAt(GameObject aimedAt, bool aimed)
    {
        if (aimedAt == null) return;

        RpcAimAt(aimedAt, aimed);
    }

    [ClientRpc]
    private void RpcAimAt(GameObject aimedAt, bool aimed)
    {
        AimHighlight.Paint(aimedAt, aimed);
    }

    public void OnEndTurnClicked()
    {
        if (Player.localPlayer == null)
        {
            Debug.LogWarning("GameManager: end turn clicked but there is no local player.");
            return;
        }

        if (Player.localPlayer.netId != currentTurnNetId)
        {
            Debug.LogWarning("GameManager: end turn clicked but it is not this player's turn.");
            return;
        }

        Debug.Log($"GameManager: end turn clicked by {Player.localPlayer.username}.");
        CmdEndTurn();
    }

    [Command(requiresAuthority = false)]
    public void CmdEndTurn(NetworkConnectionToClient sender = null)
    {
        if (!IsTurnOf(sender))
        {
            Debug.LogWarning($"GameManager: CmdEndTurn rejected - connection {sender?.connectionId} is not the current turn holder.");
            return;
        }

        ServerEndTurn(sender.identity.GetComponent<Player>());
    }

    [Server]
    public void ServerEndTurn(Player current)
    {
        if (matchEnded)
        {
            Debug.Log("GameManager: the match is over, so no turn is passed.");
            return;
        }

        if (current == null)
        {
            Debug.LogWarning("GameManager: ServerEndTurn called with no player.");
            return;
        }

        if (!IsTurnOf(current))
        {
            Debug.LogWarning($"GameManager: ServerEndTurn rejected - {current.username} is not the current turn holder.");
            return;
        }

        Player next = ServerFindOpponentOf(current);
        if (next == null)
        {
            Debug.LogWarning("GameManager: ServerEndTurn - no living opponent to pass the turn to.");
            return;
        }

        ReplayRecordEnd(current);

        turnCount++;
        ServerBeginTurnFor(next);
    }

    private void OnTurnOwnerChanged(uint oldNetId, uint newNetId)
    {
        if (Player.localPlayer == null) return;

        bool wasOurTurn = isOurTurn;
        currentTurnNetId = newNetId;
        RefreshTurnUI();

        if (wasOurTurn && !isOurTurn && playerHand != null)
            playerHand.ClearLocalPlayerHandOutlines();

        AimHighlight.Clear();
    }
}
