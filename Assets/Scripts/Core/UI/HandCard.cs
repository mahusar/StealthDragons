using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class HandCard : MonoBehaviour
{
    [Header("Sprite")]
    public Image image;

    [Header("Front & Back")]
    public Image cardfront;
    public Image cardback;

    [Header("Properties")]
    public Text cardName;
    public Text cost;
    public Text strength;
    public Text health;
    public Text description;
    public Text creatureType;

    [Header("Card Drag & Hover")]
    public HandCardDragHover cardDragHover;

    [Header("Outline")]
    public Image cardOutline;
    public Color readyColor;
    [HideInInspector] public int handIndex;
    [HideInInspector] public PlayerType playerType;

    // Called from PlayerHand to instantiate the cards in the player's hand
    public void AddCard(CardInfo newCard, int index, PlayerType playerT)
    {
        handIndex = index;
        playerType = playerT;

        // Enable hover for local player cards only
        cardDragHover.canHover = (playerType == PlayerType.PLAYER && Player.localPlayer != null && Player.localPlayer.isLocalPlayer);
        cardOutline.gameObject.SetActive(true); // Keep outline object active but control via color
        cardOutline.color = Color.clear; // Ensure outline is clear initially

        // Reveal card FRONT, hide card BACK
        cardfront.color = Color.white;
        cardback.color = Color.clear;

        // Set card image
        image.sprite = newCard.image;

        // Assign description, name and remaining stats
        description.text = newCard.description; // Description
        cost.text = newCard.cost; // Cost
        cardName.text = newCard.name;

        // Only set Health & Strength if CreatureCard
        if (newCard.data is CreatureCard)
        {
            health.text = ((CreatureCard)newCard.data).health.ToString();
            strength.text = ((CreatureCard)newCard.data).strength.ToString();
        }
    }

    public void AddCardBack()
    {
        cardfront.color = Color.clear;
        cardback.color = Color.white;
        cardOutline.color = Color.clear; // Reset outline for enemy card backs
    }

    // Clears the card. Called when we Play/remove a card.
    public void RemoveCard()
    {
        Destroy(gameObject);
    }

    public void UpdateFieldCardInfo(CardInfo card)
    {
        // Reveal card FRONT, hide card BACK
        cardfront.color = Color.white;
        cardback.color = Color.clear;

        // Set card image
        image.sprite = card.image;

        // Assign description, name and remaining stats
        description.text = card.description; // Description
        cost.text = card.cost; // Cost
        cardName.text = card.name;

        // Stats
        health.text = ((CreatureCard)card.data).health.ToString();
        strength.text = ((CreatureCard)card.data).strength.ToString();
    }

    // Clear outline explicitly
    public void ClearOutline()
    {
        if (cardOutline != null)
        {
            cardOutline.color = Color.clear;
        }
    }

    private void Update()
    {
        if (playerType == PlayerType.PLAYER && cardDragHover != null && Player.localPlayer != null && Player.localPlayer.isLocalPlayer)
        {
            // Only apply shine for local player during their turn
            int manaCost = cost.text.ToInt();
            if (Player.gameManager.isOurTurn)
            {
                cardDragHover.canDrag = Player.localPlayer.deck.CanPlayCard(manaCost);
                cardOutline.color = cardDragHover.canDrag ? readyColor : Color.clear;
            }
            else
            {
                cardDragHover.canDrag = false;
                cardOutline.color = Color.clear; // Clear shine when not local player's turn
            }
        }
        else if (playerType == PlayerType.ENEMY && cardDragHover != null)
        {
            cardDragHover.canDrag = false; // Enemy cards can't be dragged
            cardOutline.color = Color.clear; // No shine for enemy cards
        }
    }
}