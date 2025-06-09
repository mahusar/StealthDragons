using UnityEngine;
using Mirror;
using System.Collections;

public class PlayerHand : MonoBehaviour
{
    public GameObject panel;
    public HandCard cardPrefab;
    public Transform handContent;
    public PlayerType playerType;
    private Player player;
    private PlayerInfo enemyInfo;
    private int cardCount = 0;

    void Start()
    {
        StartCoroutine(DelayedStart());
    }
    IEnumerator DelayedStart()
    {
        // Wait until the player and deck are fully ready
        while (Player.localPlayer == null || Player.localPlayer.deck == null)
            yield return null;

        player = Player.localPlayer;

        // Wait a bit more just to be sure hand is synced
        yield return new WaitForSeconds(1f); 

        if (playerType == PlayerType.PLAYER && player.deck.spawnInitialCards)
        {
            DrawCards();
            player.deck.spawnInitialCards = false;
        }

        if (player && player.hasEnemy) enemyInfo = player.enemyInfo;

        if (IsEnemyHand())
        {
            UIUtils.BalancePrefabs(cardPrefab.gameObject, enemyInfo.handCount, handContent);
            for (int i = 0; i < enemyInfo.handCount; ++i)
            {
                HandCard slot = handContent.GetChild(i).GetComponent<HandCard>();
                slot.AddCardBack();
                cardCount = enemyInfo.handCount;
            }
        }
    }

    public void UpdateHandCards()
    {
        if (playerType != PlayerType.ENEMY || !player.hasEnemy)
        {
            Debug.Log($"PlayerHand.UpdateHandCards: Skipped. playerType: {playerType}, hasEnemy: {player.hasEnemy}");
            return;
        }

        if (enemyInfo.player == null || enemyInfo.data == null)
        {
            Debug.LogWarning("PlayerHand.UpdateHandCards: enemyInfo is invalid or player data is null.");
            return;
        }

        Debug.Log($"PlayerHand.UpdateHandCards: Updating enemy hand. enemyInfo.handCount: {enemyInfo.handCount}, handContent.childCount: {handContent.childCount}");
        UIUtils.BalancePrefabs(cardPrefab.gameObject, enemyInfo.handCount, handContent);
        for (int i = 0; i < enemyInfo.handCount; ++i)
        {
            HandCard slot = handContent.GetChild(i).GetComponent<HandCard>();
            slot.AddCardBack();
            slot.handIndex = i;
            Debug.Log($"PlayerHand.UpdateHandCards: Added card back at index {i}");
        }
    }


    //Draw start
    void DrawCards()
    {
        if (playerType == PlayerType.PLAYER && player != null && player.isLocalPlayer)
        {
            // Only draw if we haven't drawn initial cards yet
            if (player.deck.spawnInitialCards)
            {
                player.deck.CmdDrawInitialCards();
                player.deck.spawnInitialCards = false;
            }
        }
    }
    public void UpdateHandCardsLocal()
    {
        if (playerType != PlayerType.PLAYER || !player.isLocalPlayer)
            return;

        Debug.Log($"PlayerHand.UpdateHandCardsLocal: Updating local hand. hand count: {player.deck.hand.Count}");
        UIUtils.BalancePrefabs(cardPrefab.gameObject, player.deck.hand.Count, handContent);
        for (int i = 0; i < player.deck.hand.Count; ++i)
        {
            HandCard slot = handContent.GetChild(i).GetComponent<HandCard>();
            slot.AddCard(player.deck.hand[i], i, playerType);
        }
    }

    //Draw turn
    public void DrawTurnCard()
    {
        if (playerType == PlayerType.PLAYER && player != null)
        {
            player.deck.CmdDrawCards(1);
        }
    }

    public void AddCard(int index)
    {
        // Only proceed if this is the local player's hand
        if (playerType != PlayerType.PLAYER || !player.isLocalPlayer)
            return;

        if (player == null || player.deck == null || index >= player.deck.hand.Count)
        {
            Debug.LogWarning($"Cannot add card at index {index}");
            return;
        }

        GameObject cardObj = Instantiate(cardPrefab.gameObject);
        cardObj.transform.SetParent(handContent, false);

        CardInfo card = player.deck.hand[index];
        HandCard slot = cardObj.GetComponent<HandCard>();
        slot.AddCard(card, index, playerType);
    }

    public void RemoveCard(int index)
    {
        Debug.Log($"PlayerHand.RemoveCard called. Index: {index}, handContent.childCount: {handContent.childCount}, playerType: {playerType}");
        if (index < 0 || index >= handContent.childCount)
        {
            Debug.LogWarning($"PlayerHand.RemoveCard: Invalid index {index}. childCount: {handContent.childCount}");
            return;
        }

        HandCard slot = handContent.GetChild(index).GetComponent<HandCard>();
        slot.RemoveCard();
        Debug.Log($"PlayerHand.RemoveCard: Removed card at index {index}");

        // Update handIndex for subsequent cards
        for (int i = index + 1; i < handContent.childCount; ++i)
        {
            HandCard nextSlot = handContent.GetChild(i).GetComponent<HandCard>();
            if (nextSlot.handIndex > index)
            {
                nextSlot.handIndex--;
                Debug.Log($"PlayerHand.RemoveCard: Decremented handIndex for slot {i} to {nextSlot.handIndex}");
            }
        }
    }
    public void ClearLocalPlayerHandOutlines()
    {
        if (playerType != PlayerType.PLAYER || !player.isLocalPlayer)
            return;

        foreach (Transform child in handContent)
        {
            HandCard card = child.GetComponent<HandCard>();
            if (card != null)
            {
                card.ClearOutline();
            }
        }
    }

    // bool IsEnemyHand() => player && player.hasEnemy && player.deck.hand.Count == 7 && playerType == PlayerType.ENEMY && enemyInfo.handCount != cardCount;

    bool IsEnemyHand() => player && player.hasEnemy && playerType == PlayerType.ENEMY;
    bool IsPlayerHand() => player && player.deck.spawnInitialCards && playerType == PlayerType.PLAYER;
}