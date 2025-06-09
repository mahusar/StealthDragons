using UnityEngine;
using Mirror;
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

    [HideInInspector] public bool isHovering = false;
    [HideInInspector] public bool isHoveringField = false;
    [HideInInspector] public bool isSpawning = false;

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
        OutcomeUI outcomeUI = FindObjectOfType<OutcomeUI>();
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
        DisconnectUI disconnectUI = FindObjectOfType<DisconnectUI>();
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

    public void StartGameForPlayer(NetworkIdentity firstPlayerIdentity)
    {
        Debug.Log("GameManager: StartGameForPlayer called on server.");
        turnCount = 1;
        RpcStartGame(firstPlayerIdentity);
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

        bool isFirstPlayer = Player.localPlayer.netIdentity == firstPlayerIdentity;
        isOurTurn = isFirstPlayer;

        if (isFirstPlayer)
        {
            endTurnButton.SetActive(true);
            StartCoroutine(TurnStartSequence()); // Auto-start first turn
            Debug.Log($"GameManager: Game started for {Player.localPlayer.username}, endTurnButton active, mana: {Player.localPlayer.mana}");
        }
        else
        {
            endTurnButton.SetActive(false);
            Debug.Log($"GameManager: {Player.localPlayer.username} is not first player, endTurnButton inactive.");
        }
    }

    [Command(requiresAuthority = false)]
    public void CmdOnCardHover(float moveBy, int index)
    {
        if (enemyHand.handContent.transform.childCount > 0 && isServer)
            RpcCardHover(moveBy, index);
    }

    [ClientRpc]
    public void RpcCardHover(float moveBy, int index)
    {
        if (!isHovering)
        {
            HandCard card = enemyHand.handContent.transform.GetChild(index).GetComponent<HandCard>();
            card.transform.localPosition = new Vector2(card.transform.localPosition.x, moveBy);
        }
    }

    [Command(requiresAuthority = false)]
    public void CmdOnFieldCardHover(GameObject cardObject, bool activateShine, bool targeting)
    {
        if (isServer)
            RpcFieldCardHover(cardObject, activateShine, targeting);
    }

    [ClientRpc]
    public void RpcFieldCardHover(GameObject cardObject, bool activateShine, bool targeting)
    {
        if (!isHoveringField)
        {
            FieldCard card = cardObject.GetComponent<FieldCard>();
            Color shine = activateShine ? card.hoverColor : Color.clear;
            card.shine.color = targeting ? card.targetColor : shine;
        }
    }

    [Command(requiresAuthority = false)]
    public void CmdEndTurn()
    {
        RpcSetTurn();
    }

    [ClientRpc]
    public void RpcSetTurn()
    {
        if (Player.localPlayer == null || playerHand == null)
        {
            return;
        }

        bool wasOurTurn = isOurTurn;
        isOurTurn = !isOurTurn;
        endTurnButton.SetActive(isOurTurn);

        if (wasOurTurn && !isOurTurn)
        {
            playerHand.ClearLocalPlayerHandOutlines();
        }

        if (isOurTurn)
        {
            StartCoroutine(TurnStartSequence());
        }
    }

    private IEnumerator TurnStartSequence()
    {
        yield return null;

        if (Player.gameManager.playerField != null)
        {
            Player.gameManager.playerField.UpdateFieldCards();
        }

        if (Player.localPlayer.deck.deckList.Count > 0)
        {
            playerHand.DrawTurnCard();
        }
        else
        {
            Debug.Log("Deck is empty - cannot draw card");
        }

        Player.localPlayer.deck.CmdStartNewTurn();
    }
}