using UnityEngine;
using Mirror;
using TMPro;
using System.Collections;
using System.Collections.Generic;

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
    public PlayerField playerField;
    public PlayerField enemyField;

    [Header("Turn Management")]
    public GameObject endTurnButton;
    [HideInInspector] public bool isOurTurn = false;
    [SyncVar, HideInInspector] public int turnCount = 1;

    [Header("Turn Timer")]
    [Tooltip("Seconds a player gets per turn before the turn is passed automatically.")]
    public float turnSeconds = 60f;
    [Tooltip("Seconds remaining at which the countdown turns to the warning colour.")]
    public float turnWarningSeconds = 10f;
    public TMP_Text turnTimerText;
    public Color turnTimerColor = Color.white;
    public Color turnTimerWarningColor = new Color(1f, 0.35f, 0.35f);

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

    public SyncListPlayerInfo players = new SyncListPlayerInfo();
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
            Debug.LogWarning("GameManager: starting a PRACTICE match — no stake, no payout.");
        }
        else
        {
            DragonatorWallet wallet = FindAnyObjectByType<DragonatorWallet>();
            if (wallet == null || !wallet.BothPlayersValidated())
            {
                Debug.LogWarning("GameManager: Cannot start — bets not validated yet.");
                return;
            }
        }

        if (currentTurnNetId != 0)
        {
            Debug.LogWarning("GameManager: StartGameForPlayer called twice — ignoring.");
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

        ServerBeginTurnFor(first);
        RpcStartGame(firstPlayerIdentity);
    }

    [Server]
    private void ServerBeginTurnFor(Player player)
    {
        currentTurnNetId = player.netId;

        if (player.mana < player.maxMana)
        {
            player.currentMax++;
            player.mana = player.currentMax;
        }

        foreach (FieldCard card in FindObjectsByType<FieldCard>(FindObjectsSortMode.None))
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

        Debug.Log($"GameManager: {current.username} ran out of time after {turnSeconds}s — passing the turn.");
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
        if (endTurnButton != null) endTurnButton.SetActive(isOurTurn);
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
            Debug.LogWarning("GameManager: CmdOnFieldCardHover rejected — null card object.");
            return;
        }

        RpcFieldCardHover(cardObject, activateShine, targeting);
    }

    [ClientRpc]
    public void RpcFieldCardHover(GameObject cardObject, bool activateShine, bool targeting)
    {
        if (isHoveringField) return;
        if (cardObject == null) return;

        FieldCard card = cardObject.GetComponent<FieldCard>();
        if (card == null || card.shine == null) return;

        Color shine = activateShine ? card.hoverColor : Color.clear;
        card.shine.color = targeting ? card.targetColor : shine;
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
            Debug.LogWarning($"GameManager: CmdEndTurn rejected — connection {sender?.connectionId} is not the current turn holder.");
            return;
        }

        ServerEndTurn(sender.identity.GetComponent<Player>());
    }

    [Server]
    public void ServerEndTurn(Player current)
    {
        if (current == null)
        {
            Debug.LogWarning("GameManager: ServerEndTurn called with no player.");
            return;
        }

        if (!IsTurnOf(current))
        {
            Debug.LogWarning($"GameManager: ServerEndTurn rejected — {current.username} is not the current turn holder.");
            return;
        }

        Player next = ServerFindOpponentOf(current);
        if (next == null)
        {
            Debug.LogWarning("GameManager: ServerEndTurn — no living opponent to pass the turn to.");
            return;
        }

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
    }
}
