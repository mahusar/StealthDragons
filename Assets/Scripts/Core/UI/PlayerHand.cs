using UnityEngine;
using Mirror;
using System.Collections;

public class PlayerHand : MonoBehaviour
{
    public const float hoverLiftY = -25f;

    public GameObject panel;
    public HandCard cardPrefab;
    public Transform handContent;
    public SeatKind playerType;
    private Player player;
    private PlayerInfo enemyInfo;
    private int cardCount = 0;

    void Start()
    {
        StartCoroutine(DelayedStart());
    }
    IEnumerator DelayedStart()
    {
        while (Player.localPlayer == null || Player.localPlayer.deck == null)
            yield return null;

        player = Player.localPlayer;

        if (playerType == SeatKind.PLAYER && player.deck.spawnInitialCards)
        {
            DrawCards();
            player.deck.spawnInitialCards = false;
        }

        if (playerType == SeatKind.ENEMY)
        {
            while (!player.hasEnemy)
            {
                player.UpdateEnemyInfo();
                yield return new WaitForSeconds(0.5f);
            }

            enemyInfo = player.enemyInfo;

            if (!Wired()) yield break;

            SlotFiller.Fill(cardPrefab.gameObject, enemyInfo.handCount, handContent);
            for (int i = 0; i < enemyInfo.handCount; ++i)
            {
                HandCard slot = handContent.GetChild(i).GetComponent<HandCard>();
                slot.AddCardBack();
                cardCount = enemyInfo.handCount;
            }
        }
        else
        {
            if (player.hasEnemy) enemyInfo = player.enemyInfo;
        }
    }

    public void UpdateHandCards()
    {
        if (playerType != SeatKind.ENEMY || !player.hasEnemy)
        {
            Debug.Log($"PlayerHand.UpdateHandCards: Skipped. playerType: {playerType}, hasEnemy: {player.hasEnemy}");
            return;
        }

        if (enemyInfo.player == null || enemyInfo.data == null)
        {
            Debug.LogWarning("PlayerHand.UpdateHandCards: enemyInfo is invalid or player data is null.");
            return;
        }

        if (!Wired()) return;

        Debug.Log($"PlayerHand.UpdateHandCards: Updating enemy hand. enemyInfo.handCount: {enemyInfo.handCount}, handContent.childCount: {handContent.childCount}");
        SlotFiller.Fill(cardPrefab.gameObject, enemyInfo.handCount, handContent);
        for (int i = 0; i < enemyInfo.handCount; ++i)
        {
            HandCard slot = handContent.GetChild(i).GetComponent<HandCard>();
            slot.AddCardBack();
            slot.handIndex = i;
            Debug.Log($"PlayerHand.UpdateHandCards: Added card back at index {i}");
        }

        SetHoveredCard(-1);
    }

    public void SetHoveredCard(int index)
    {
        if (handContent == null) return;

        for (int i = 0; i < handContent.childCount; ++i)
        {
            Transform slot = handContent.GetChild(i);
            float y = i == index ? hoverLiftY : 0f;
            slot.localPosition = new Vector2(slot.localPosition.x, y);
        }
    }

    void DrawCards()
    {
        if (playerType == SeatKind.PLAYER && player != null && player.isLocalPlayer)
        {
            if (player.deck.spawnInitialCards)
            {
                player.deck.CmdDrawInitialCards();
                player.deck.spawnInitialCards = false;
            }
        }
    }
    public bool IsReady => player != null && player.deck != null;

    private bool Wired()
    {
        if (cardPrefab != null && handContent != null) return true;

        Debug.LogWarning($"PlayerHand ({playerType}) on {gameObject.name} is missing " +
                         $"{(cardPrefab == null ? "cardPrefab" : "handContent")} - this hand cannot be drawn.");
        return false;
    }

    public void UpdateHandCardsLocal()
    {
        if (playerType != SeatKind.PLAYER)
            return;

        if (player == null || player.deck == null || !player.isLocalPlayer)
            return;

        if (!Wired()) return;

        Debug.Log($"PlayerHand.UpdateHandCardsLocal: Updating local hand. hand count: {player.deck.hand.Count}");
        SlotFiller.Fill(cardPrefab.gameObject, player.deck.hand.Count, handContent);
        for (int i = 0; i < player.deck.hand.Count; ++i)
        {
            HandCard slot = handContent.GetChild(i).GetComponent<HandCard>();
            slot.AddCard(player.deck.hand[i], i, playerType);
        }
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
        if (playerType != SeatKind.PLAYER || !player.isLocalPlayer)
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

    bool IsEnemyHand() => player && player.hasEnemy && playerType == SeatKind.ENEMY;
    bool IsPlayerHand() => player && player.deck.spawnInitialCards && playerType == SeatKind.PLAYER;
}
