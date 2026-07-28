using UnityEngine;
using Mirror;
using System.Collections;

public class Deck : NetworkBehaviour
{
    [Header("Player")]
    public Player player;
    [HideInInspector] public int deckSize = 30;
    [HideInInspector] public int handSize = 7;
    [Header("Decks")]
    public SyncListCard deckList = new SyncListCard();
    public SyncListCard graveyard = new SyncListCard();
    public SyncListCard hand = new SyncListCard();
    [Header("Battlefield")]
    public SyncListCard playerField = new SyncListCard();
    [Header("Starting Deck")]
    public CardAndAmount[] startingDeck;
    [HideInInspector] public bool spawnInitialCards = true;
    [SyncVar(hook = nameof(OnEnemyHandCountChanged))]
    public int enemyHandCount = 0;

    void OnEnemyHandCountChanged(int oldCount, int newCount)
    {
        if (!isClient || Player.localPlayer == null || Player.gameManager == null || Player.gameManager.enemyHand == null)
            return;

        if (!Player.localPlayer.hasEnemy)
        {
            StartCoroutine(WaitForEnemyThenUpdate());
            return;
        }

        Player.gameManager.enemyHand.UpdateHandCards();
    }

    private IEnumerator WaitForEnemyThenUpdate()
    {
        while (Player.localPlayer == null)
            yield return null;

        while (!Player.localPlayer.hasEnemy)
        {
            Player.localPlayer.UpdateEnemyInfo();
            yield return new WaitForSeconds(0.5f);
        }

        Debug.Log("WaitForEnemyThenUpdate: Enemy found, updating hand.");

        if (Player.gameManager?.enemyHand != null)
            Player.gameManager.enemyHand.UpdateHandCards();
    }
    private bool handCallbackRegistered;

    private void RegisterHandCallback()
    {
        if (handCallbackRegistered) return;
        handCallbackRegistered = true;
        hand.Callback += OnHandChanged;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        RegisterHandCallback();
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (isLocalPlayer)
        {
            RegisterHandCallback();
            if (Player.gameManager == null)
            {
                Player.gameManager = FindObjectOfType<GameManager>();
            }
            StartCoroutine(RebuildHandWhenUIReady());
        }
    }

    private IEnumerator RebuildHandWhenUIReady()
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (Player.gameManager == null)
                Player.gameManager = FindObjectOfType<GameManager>();

            if (Player.gameManager?.playerHand != null && Player.gameManager.playerHand.IsReady)
            {
                Player.gameManager.playerHand.UpdateHandCardsLocal();
                yield break;
            }
            yield return null;
        }

        Debug.LogWarning($"Deck: playerHand never became available for {player?.username}; hand not rendered.");
    }

    void OnHandChanged(SyncList<CardInfo>.Operation op, int itemIndex, CardInfo oldItem, CardInfo newItem)
    {
        if (isServer) enemyHandCount = hand.Count;

        if (isLocalPlayer && Player.gameManager?.playerHand != null)
        {
            Player.gameManager.playerHand.UpdateHandCardsLocal();
        }
    }

    #region Load Deck

    public override void OnStartLocalPlayer()
    {
        CmdLoadDeck();
    }

    private bool deckLoaded = false;
    private bool initialCardsDrawn = false;

    [Command]
    public void CmdLoadDeck()
    {
        ServerLoadDeck();
    }

    [Server]
    public void ServerLoadDeck()
    {
        if (deckLoaded)
        {
            Debug.LogWarning($"CmdLoadDeck rejected: deck already loaded for {player?.username}.");
            return;
        }
        deckLoaded = true;

        string playerId = netIdentity.netId.ToString();

        deckList.Clear();

        int totalCards = 0;
        for (int i = 0; i < startingDeck.Length; ++i)
        {
            CardAndAmount card = startingDeck[i];
            string cardName = card.card != null ? card.card.name : "null";
            totalCards += card.amount;
        }

        for (int i = 0; i < startingDeck.Length; ++i)
        {
            CardAndAmount card = startingDeck[i];
            for (int v = 0; v < card.amount; ++v)
            {
                deckList.Add(new CardInfo(card.card));
            }
        }

        deckList.Shuffle();
    }
    #endregion

    #region Draw Card
    [Command]
    public void CmdDrawInitialCards()
    {
        ServerDrawInitialCards();
    }

    [Server]
    public void ServerDrawInitialCards()
    {
        if (initialCardsDrawn)
        {
            Debug.LogWarning($"CmdDrawInitialCards rejected: opening hand already drawn for {player?.username}.");
            return;
        }
        initialCardsDrawn = true;

        hand.Clear();
        for (int i = 0; i < handSize && deckList.Count > 0; i++)
        {
            hand.Add(deckList[0]);
            deckList.RemoveAt(0);
        }

        Debug.Log($"CmdDrawInitialCards: {player?.username} drew {hand.Count} cards, {deckList.Count} left in deck.");
    }

    [Server]
    public void ServerDrawCards(int amount)
    {
        int drawn = 0;
        for (int i = 0; i < amount && deckList.Count > 0; i++)
        {
            hand.Add(deckList[0]);
            deckList.RemoveAt(0);
            drawn++;
        }

        Debug.Log($"ServerDrawCards: {player?.username} drew {drawn}, hand now {hand.Count}, {deckList.Count} left in deck.");
    }

    #endregion

    #region Play Card

    public bool CanPlayCard(int manaCost)
    {
        return player.mana >= manaCost && player.health > 0;
    }

    [Command]
    public void CmdPlayCard(int index)
    {
        ServerPlayCard(index);
    }

    [Server]
    public void ServerPlayCard(int index)
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null || !gm.IsTurnOf(player))
        {
            Debug.LogWarning($"CmdPlayCard rejected: not {player?.username}'s turn.");
            return;
        }

        if (index < 0 || index >= hand.Count)
        {
            Debug.LogWarning($"CmdPlayCard rejected: index {index} out of range (hand {hand.Count}).");
            return;
        }

        CardInfo card = hand[index];
        if (!(card.data is CreatureCard creature))
        {
            Debug.LogWarning($"CmdPlayCard rejected: card at {index} is not a creature.");
            return;
        }

        int manaCost = card.data.cost;
        if (!CanPlayCard(manaCost))
        {
            Debug.LogWarning($"CmdPlayCard rejected: {player.username} has {player.mana} mana, needs {manaCost}.");
            return;
        }

        player.combat.ServerChangeMana(-manaCost);

        GameObject boardCard = Instantiate(creature.cardPrefab.gameObject);
        FieldCard newCard = boardCard.GetComponent<FieldCard>();
        newCard.card = new CardInfo(card.data);
        newCard.cardName.text = card.name;
        newCard.health = creature.health;
        newCard.strength = creature.strength;
        newCard.image.sprite = card.image;
        newCard.image.color = Color.white;
        newCard.owner = player;

        newCard.taunt = creature.hasTaunt;
        if (creature.hasTaunt)
        {
            player.tauntCount++;
            Debug.Log($"Player {player.username}: Taunt creature played. tauntCount: {player.tauntCount}");
        }

        if (creature.hasCharge) newCard.waitTurn = 0;

        newCard.cardHover.UpdateFieldCardInfo(card);

        NetworkServer.Spawn(boardCard);

        hand.RemoveAt(index);

        if (isServer) RpcPlayCard(boardCard);
    }

    [ClientRpc]
    public void RpcPlayCard(GameObject boardCard)
    {
        if (boardCard == null)
        {
            Debug.LogWarning("Deck: RpcPlayCard ignored — the board card is not spawned on this client.");
            return;
        }

        if (Player.gameManager == null)
            Player.gameManager = FindObjectOfType<GameManager>();

        GameManager gm = Player.gameManager;
        if (gm == null)
        {
            Debug.LogWarning("Deck: RpcPlayCard ignored — no GameManager on this client yet.");
            return;
        }

        FieldCard fieldCard = boardCard.GetComponent<FieldCard>();
        if (fieldCard == null)
        {
            Debug.LogWarning($"Deck: RpcPlayCard ignored — {boardCard.name} has no FieldCard component.");
            return;
        }

        bool mine = isLocalPlayer;
        PlayerField field = mine ? gm.playerField : gm.enemyField;
        if (field == null || field.content == null)
        {
            Debug.LogWarning($"Deck: RpcPlayCard ignored — the {(mine ? "player" : "enemy")} field is not assigned on this client.");
            return;
        }

        fieldCard.casterType = mine ? Target.FRIENDLIES : Target.ENEMIES;
        boardCard.transform.SetParent(field.content, false);
    }
    #endregion
}
