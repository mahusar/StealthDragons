using UnityEngine;
using Mirror;

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
        if (!isClient || player == null || !player.hasEnemy || Player.gameManager == null || Player.gameManager.enemyHand == null)
        {
            Debug.LogWarning($"OnEnemyHandCountChanged: Skipped. isClient: {isClient}, player: {(player != null)}, hasEnemy: {(player != null ? player.hasEnemy : false)}, gameManager: {(Player.gameManager != null)}, enemyHand: {(Player.gameManager != null ? Player.gameManager.enemyHand != null : false)}");
            return;
        }

        Player.gameManager.enemyHand.UpdateHandCards();
    }
    public override void OnStartServer()
    {
        base.OnStartServer();
        hand.Callback += OnHandChanged;
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (isLocalPlayer)
        {
            hand.Callback += OnHandChanged;
            if (Player.gameManager == null)
            {
                Player.gameManager = FindObjectOfType<GameManager>();
    //            Debug.Log($"Player {netIdentity.netId}: OnStartClient: Initialized GameManager.");
            }
    //        Debug.Log($"Player {netIdentity.netId}: OnStartClient calling CmdRequestHandState");
            CmdRequestHandState();
        }
    }

    [Command]
    private void CmdRequestHandState()
    {
        string playerId = netIdentity.netId.ToString();
   //     Debug.Log($"Player {playerId}: CmdRequestHandState called. hand count: {hand.Count}, connectionToClient: {(connectionToClient != null ? connectionToClient.identity.netId.ToString() : "null")}");
        for (int i = 0; i < hand.Count; i++)
        {
           
            TargetAddCardToHand(connectionToClient, i, hand[i]);
        }
    }

    void OnHandChanged(SyncList<CardInfo>.Operation op, int itemIndex, CardInfo oldItem, CardInfo newItem)
    {
        string playerId = netIdentity.netId.ToString();
  //      Debug.Log($"Player {playerId}: OnHandChanged. Operation: {op}, Index: {itemIndex}, hand count: {hand.Count}");
        enemyHandCount = hand.Count;
        if (isLocalPlayer && Player.gameManager?.playerHand != null)
        {
   //         Debug.Log($"Player {playerId}: Updating local playerHand UI.");
            Player.gameManager.playerHand.UpdateHandCardsLocal();
        }
    }

    #region Load Deck

    public override void OnStartLocalPlayer()
    {
        CmdLoadDeck();
    }

    [Command]
    public void CmdLoadDeck()
    {
        string playerId = netIdentity.netId.ToString();

        // Clear existing cards
        deckList.Clear();
   //     Debug.Log($"Player {playerId}: Deck cleared. Initial deckList count: {deckList.Count}");

        // Log startingDeck details
   //     Debug.Log($"Player {playerId}: startingDeck has {startingDeck.Length} entries");
        int totalCards = 0;
        for (int i = 0; i < startingDeck.Length; ++i)
        {
            CardAndAmount card = startingDeck[i];
            string cardName = card.card != null ? card.card.name : "null";
    //        Debug.Log($"Player {playerId}: Card {i} - Name: {cardName}, Amount: {card.amount}");
            totalCards += card.amount;
        }
    //    Debug.Log($"Player {playerId}: Expected total cards from startingDeck: {totalCards}");

        // Add all cards
        for (int i = 0; i < startingDeck.Length; ++i)
        {
            CardAndAmount card = startingDeck[i];
            for (int v = 0; v < card.amount; ++v)
            {
                deckList.Add(new CardInfo(card.card));
            }
        }
   //     Debug.Log($"Player {playerId}: Cards added to deckList. Total deckList count: {deckList.Count}");

        // Shuffle the deck
        deckList.Shuffle();
   //     Debug.Log($"Player {playerId}: Deck shuffled.");
    }
    #endregion

    #region Draw Card
    [Command]
    public void CmdDrawInitialCards()
    {
        string playerId = netIdentity.netId.ToString();
    //    Debug.Log($"Player {playerId}: CmdDrawInitialCards called. deckList count: {deckList.Count}, hand count: {hand.Count}, connectionToClient: {(connectionToClient != null ? connectionToClient.identity.netId.ToString() : "null")}");
        hand.Clear();
        for (int i = 0; i < 7 && deckList.Count > 0; i++)
        {
            CardInfo drawnCard = deckList[0];
            hand.Add(drawnCard);
            deckList.RemoveAt(0);
            
            TargetAddCardToHand(connectionToClient, i, drawnCard);
        }
   //     Debug.Log($"Player {playerId}: CmdDrawInitialCards finished. hand count: {hand.Count}, deckList count: {deckList.Count}");
    }

    [TargetRpc]
    private void TargetAddCardToHand(NetworkConnection target, int index, CardInfo card)
    {
        string playerId = netIdentity.netId.ToString();
    //    Debug.Log($"Player {playerId}: TargetAddCardToHand called. Index: {index},  GameManager: {(Player.gameManager != null ? "set" : "null")}, playerHand: {(Player.gameManager?.playerHand != null ? "set" : "null")}");
        if (Player.gameManager == null || Player.gameManager.playerHand == null)
        {
    //        Debug.LogError($"Player {playerId}: TargetAddCardToHand failed: GameManager or playerHand is null.");
            return;
        }
        Player.gameManager.playerHand.AddCard(index);
    }

    [Command]
    public void CmdDrawCards(int amount)
    {
        for (int i = 0; i < amount && deckList.Count > 0; i++)
        {
            // Get top card
            CardInfo drawnCard = deckList[0];

            // Add to hand and remove from deck
            hand.Add(drawnCard);
            deckList.RemoveAt(0);

            // Update specific client
            TargetAddCardToHand(connectionToClient, hand.Count - 1, drawnCard);
        }
    }

    #endregion

    #region Play Card

    public bool CanPlayCard(int manaCost)
    {
        return player.mana >= manaCost && player.health > 0;
    }

    [Command]
    public void CmdPlayCard(CardInfo card, int index)
    {
        CreatureCard creature = (CreatureCard)card.data;
        GameObject boardCard = Instantiate(creature.cardPrefab.gameObject);
        FieldCard newCard = boardCard.GetComponent<FieldCard>();
        newCard.card = new CardInfo(card.data);
        newCard.cardName.text = card.name;
        newCard.health = creature.health;
        newCard.strength = creature.strength;
        newCard.image.sprite = card.image;
        newCard.image.color = Color.white;
        newCard.owner = player;

        // Set taunt property
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

        if (isServer) RpcPlayCard(boardCard, index);
    }

    [ClientRpc]
    public void RpcPlayCard(GameObject boardCard, int index)
    {
        string playerId = netIdentity.netId.ToString();
   //     Debug.Log($"Player {playerId}: RpcPlayCard called. Index: {index}, isSpawning: {Player.gameManager.isSpawning}, hasEnemy: {player.hasEnemy}, isLocalPlayer: {isLocalPlayer}");
        if (Player.gameManager.isSpawning && isLocalPlayer)
        {
            boardCard.GetComponent<FieldCard>().casterType = Target.FRIENDLIES;
            boardCard.transform.SetParent(Player.gameManager.playerField.content, false);
            // Remove explicit RemoveCard call; rely on OnHandChanged
       //    Debug.Log($"Player {playerId}: RpcPlayCard: Skipping explicit RemoveCard; OnHandChanged will update UI.");
            Player.gameManager.isSpawning = false;
        }
        else if (player.hasEnemy && !isLocalPlayer)
        {
            boardCard.GetComponent<FieldCard>().casterType = Target.ENEMIES;
            boardCard.transform.SetParent(Player.gameManager.enemyField.content, false);
      //      Debug.Log($"Player {playerId}: RpcPlayCard: Enemy card played; UpdateHandCards will handle UI.");
        }
    }
    #endregion

    #region Turn 
    [Command]
    public void CmdStartNewTurn()
    {
        if (player.mana < player.maxMana)
        {
            player.currentMax++;
            player.mana = player.currentMax;
            Debug.LogError("Here");
        }
    }
    #endregion

}
