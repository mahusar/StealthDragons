using UnityEngine;
using Mirror;
using System.Collections;
using System.Collections.Generic;

public class Deck : NetworkBehaviour
{
    [Header("Player")]
    public Player player;
    [HideInInspector] public int deckSize = 30;
    [HideInInspector] public int handSize = 7;
    [Header("Decks")]
    public CardList deckList = new CardList();
    public CardList graveyard = new CardList();
    public CardList hand = new CardList();
    [Header("Battlefield")]
    public CardList playerField = new CardList();
    [Header("Starting Deck")]
    public DeckEntry[] startingDeck;
    [HideInInspector] public bool spawnInitialCards = true;

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

        deckList.Callback += OnDeckListChanged;
        graveyard.Callback += OnGraveyardChanged;
        ServerPublishCounts();
    }

    private void OnDeckListChanged(SyncList<CardInfo>.Operation op, int itemIndex, CardInfo oldItem, CardInfo newItem)
    {
        ServerPublishCounts();
    }

    private void OnGraveyardChanged(SyncList<CardInfo>.Operation op, int itemIndex, CardInfo oldItem, CardInfo newItem)
    {
        ServerPublishCounts();
    }

    [Server]
    private void ServerPublishCounts()
    {
        if (player == null) return;

        player.handCount = hand.Count;
        player.deckCount = deckList.Count;
        player.graveCount = graveyard.Count;
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

    public List<CardInfo> StartingComposition()
    {
        List<CardInfo> composition = new List<CardInfo>();

        for (int i = 0; i < startingDeck.Length; ++i)
        {
            DeckEntry card = startingDeck[i];
            for (int v = 0; v < card.amount; ++v) composition.Add(new CardInfo(card.card));
        }

        return composition;
    }

    private bool recordingDealtOrder;

    private void ClientRecordDealtOrder()
    {
        if (!isLocalPlayer) return;
        if (recordingDealtOrder) return;

        recordingDealtOrder = true;
        StartCoroutine(RecordDealtOrderWhenStable());
    }

    private IEnumerator RecordDealtOrderWhenStable()
    {
        List<CardInfo> composition = StartingComposition();
        float deadline = Time.realtimeSinceStartup + 20f;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (hand.Count > 0 && hand.Count + deckList.Count == composition.Count)
            {
                List<CardInfo> order = new List<CardInfo>();
                for (int i = 0; i < hand.Count; i++) order.Add(hand[i]);
                for (int i = 0; i < deckList.Count; i++) order.Add(deckList[i]);

                LocalShuffleProof.RememberDeal(order, netIdentity.netId, composition);
                CmdAttestDealtOrder(CardShuffle.Fingerprint(order));
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("Deck: the dealt order never settled; this deal cannot be re-checked locally.");
    }

    void OnHandChanged(SyncList<CardInfo>.Operation op, int itemIndex, CardInfo oldItem, CardInfo newItem)
    {
        if (isServer) ServerPublishCounts();

        ClientRecordDealtOrder();

        if (isLocalPlayer && Player.gameManager?.playerHand != null)
        {
            Player.gameManager.playerHand.UpdateHandCardsLocal();
        }
    }

    #region Load Deck

    public override void OnStartLocalPlayer()
    {
        CmdLoadDeck();

        if (!seedSubmitted)
        {
            seedSubmitted = true;
            StartCoroutine(SubmitShuffleSeedWhenCommitted());
        }
    }

    private bool deckLoaded = false;
    private bool initialCardsDrawn = false;
    private bool dealt = false;
    private bool seedSubmitted = false;

    [Command]
    public void CmdSubmitSeedCommitment(byte[] hash)
    {
        MatchFairness.AddSeedCommitment(netIdentity.netId, hash);
    }

    [Command]
    public void CmdSubmitShuffleSeed(byte[] seed)
    {
        MatchFairness.AddClientSeed(netIdentity.netId, seed);
    }

    [Command]
    public void CmdAttestDealtOrder(string fingerprint)
    {
        if (!MatchFairness.AddDealtOrder(netIdentity.netId, fingerprint)) return;

        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm != null) gm.ServerPublishDealtOrders();
    }

    private IEnumerator SubmitShuffleSeedWhenCommitted()
    {
        float deadline = Time.realtimeSinceStartup + 15f;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (Player.gameManager == null)
                Player.gameManager = FindObjectOfType<GameManager>();

            if (Player.gameManager != null && !string.IsNullOrEmpty(Player.gameManager.shuffleCommitment))
                break;

            yield return null;
        }

        if (Player.gameManager == null || string.IsNullOrEmpty(Player.gameManager.shuffleCommitment))
        {
            Debug.LogWarning("Deck: no shuffle commitment appeared; this deal cannot be verified.");
            yield break;
        }

        byte[] seed = CardShuffle.NewSeed(CardShuffle.ClientSeedBytes);
        LocalShuffleProof.Remember(Player.gameManager.shuffleCommitment, seed);

        CmdSubmitSeedCommitment(CardShuffle.Commitment(seed));
        Debug.Log("Deck: committed to my shuffle seed without revealing it.");

        deadline = Time.realtimeSinceStartup + 30f;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (Player.gameManager == null)
            {
                Debug.LogWarning("Deck: the GameManager vanished before my seed could be revealed.");
                yield break;
            }

            if (Player.gameManager.shuffleSealed)
            {
                CmdSubmitShuffleSeed(seed);
                Debug.Log("Deck: revealed my shuffle seed once every player was locked in.");
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("Deck: the seed commitments were never sealed; my seed stays unrevealed.");
    }

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
            DeckEntry card = startingDeck[i];
            string cardName = card.card != null ? card.card.name : "null";
            totalCards += card.amount;
        }

        for (int i = 0; i < startingDeck.Length; ++i)
        {
            DeckEntry card = startingDeck[i];
            for (int v = 0; v < card.amount; ++v)
            {
                deckList.Add(new CardInfo(card.card));
            }
        }
    }

    [Server]
    public void ServerShuffleAndDeal()
    {
        if (!deckLoaded) ServerLoadDeck();

        if (dealt)
        {
            Debug.LogWarning($"ServerShuffleAndDeal: {player?.username} was already dealt - ignoring.");
            return;
        }
        dealt = true;

        List<CardInfo> ordered = deckList.ToList();
        CardShuffle.Shuffle(ordered, MatchFairness.SeedFor(netIdentity.netId));

        deckList.Clear();
        for (int i = 0; i < ordered.Count; i++) deckList.Add(ordered[i]);

        Debug.Log($"ServerShuffleAndDeal: {player?.username} deck order fingerprint {CardShuffle.Fingerprint(ordered)}");

        ServerDrawInitialCards();
    }
    #endregion

    #region Draw Card
    [Command]
    public void CmdDrawInitialCards()
    {
        Debug.Log($"CmdDrawInitialCards ignored: the server deals every opening hand once the shuffle is settled ({player?.username}).");
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
    public void CmdPlayCard(int index, int slot)
    {
        ServerPlayCard(index, slot);
    }

    [Server]
    public void ServerPlayCard(int index, int slot = -1)
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
        BoardCard newCard = boardCard.GetComponent<BoardCard>();
        newCard.card = new CardInfo(card.data);
        newCard.cardName.text = card.displayName;
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

        newCard.shielded = creature.hasShield;

        newCard.cardHover.UpdateFieldCardInfo(card);

        NetworkServer.Spawn(boardCard);

        hand.RemoveAt(index);

        gm.ReplayRecordPlay(player, index, card.cardID, newCard != null ? newCard.netId : 0);

        if (isServer) RpcPlayCard(boardCard, slot);
    }

    [Command]
    public void CmdCastSpell(int index, uint targetNetId)
    {
        ServerCastSpell(index, targetNetId);
    }

    [Server]
    public void ServerCastSpell(int index, uint targetNetId)
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null || !gm.IsTurnOf(player))
        {
            Debug.LogWarning($"CmdCastSpell rejected: not {player?.username}'s turn.");
            return;
        }

        if (index < 0 || index >= hand.Count)
        {
            Debug.LogWarning($"CmdCastSpell rejected: index {index} out of range (hand {hand.Count}).");
            return;
        }

        CardInfo card = hand[index];
        if (!(card.data is SpellCard spell))
        {
            Debug.LogWarning($"CmdCastSpell rejected: card at {index} is not a spell.");
            return;
        }

        int manaCost = card.data.cost;
        if (!CanPlayCard(manaCost))
        {
            Debug.LogWarning($"CmdCastSpell rejected: {player.username} has {player.mana} mana, needs {manaCost}.");
            return;
        }

        BoardCard chosen = ServerFindBoardCard(targetNetId);

        string trouble;
        if (!Spellbook.Resolve(spell, player, chosen, out trouble))
        {
            Debug.LogWarning($"CmdCastSpell rejected: {trouble}.");
            return;
        }

        player.combat.ServerChangeMana(-manaCost);

        hand.RemoveAt(index);
        graveyard.Add(card);

        gm.ReplayRecordCast(player, index, card.cardID, targetNetId);
    }

    [Server]
    private BoardCard ServerFindBoardCard(uint netId)
    {
        if (netId == 0) return null;
        if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity)) return null;

        return identity != null ? identity.GetComponent<BoardCard>() : null;
    }

    [ClientRpc]
    public void RpcPlayCard(GameObject boardCard, int slot)
    {
        if (boardCard == null)
        {
            Debug.LogWarning("Deck: RpcPlayCard ignored - the board card is not spawned on this client.");
            return;
        }

        if (Player.gameManager == null)
            Player.gameManager = FindObjectOfType<GameManager>();

        GameManager gm = Player.gameManager;
        if (gm == null)
        {
            Debug.LogWarning("Deck: RpcPlayCard ignored - no GameManager on this client yet.");
            return;
        }

        BoardCard fieldCard = boardCard.GetComponent<BoardCard>();
        if (fieldCard == null)
        {
            Debug.LogWarning($"Deck: RpcPlayCard ignored - {boardCard.name} has no BoardCard component.");
            return;
        }

        bool mine = isLocalPlayer;
        Battlefield field = mine ? gm.playerField : gm.enemyField;
        if (field == null || field.content == null)
        {
            Debug.LogWarning($"Deck: RpcPlayCard ignored - the {(mine ? "player" : "enemy")} field is not assigned on this client.");
            return;
        }

        fieldCard.casterType = mine ? Target.FRIENDLIES : Target.ENEMIES;
        boardCard.transform.SetParent(field.content, false);

        if (slot >= 0 && slot < field.content.childCount) boardCard.transform.SetSiblingIndex(slot);
        CardPlayAnimator.PlayEntry(boardCard.transform);
    }
    #endregion
}
