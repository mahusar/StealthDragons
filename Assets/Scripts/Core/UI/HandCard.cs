using UnityEngine;
using UnityEngine.UI;

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
    public HandCardPointer cardDragHover;

    [Header("Outline")]
    public Image cardOutline;
    public Color readyColor;

    [Header("Keyword Text")]
    public Color plainColor = new Color(0.85f, 0.85f, 0.85f, 1f);

    [Header("Deathrattle")]
    public Text deathrattleValue;
    public Vector2 deathrattleCorner = new Vector2(132f, -212f);
    public Vector2 deathrattleSize = new Vector2(70f, 70f);
    public int deathrattleFontSize = 40;

    [HideInInspector] public int handIndex;
    [HideInInspector] public SeatKind playerType;

    public void AddCard(CardInfo newCard, int index, SeatKind playerT)
    {
        handIndex = index;
        playerType = playerT;

        if (cardDragHover != null) cardDragHover.canHover = playerType == SeatKind.PLAYER && Mine();

        if (cardOutline != null)
        {
            cardOutline.gameObject.SetActive(true);
            cardOutline.color = Color.clear;
        }

        Paint(newCard);
    }

    public void UpdateFieldCardInfo(CardInfo card)
    {
        Paint(card);
    }

    public void AddCardBack()
    {
        Face(false);
        ClearOutline();
    }

    public void RemoveCard()
    {
        Destroy(gameObject);
    }

    public void ClearOutline()
    {
        if (cardOutline != null) cardOutline.color = Color.clear;
    }

    private void Paint(CardInfo card)
    {
        Face(true);

        if (image != null) image.sprite = card.image;
        if (description != null) description.text = card.description;
        if (cost != null) cost.text = card.cost;
        if (cardName != null) cardName.text = card.displayName;

        CreatureCard creature = card.data as CreatureCard;

        ShowBody(creature != null);
        ShowDeathrattle(creature);

        if (creature == null)
        {
            if (creatureType != null) Keyword.Fit(creatureType, null);
            if (creatureType != null) creatureType.text = SpellText.Line(card.data as SpellCard);
            if (creatureType != null) creatureType.color = SpellText.Tint;
            return;
        }

        if (health != null) health.text = creature.health.ToString();
        if (strength != null) strength.text = creature.strength.ToString();

        if (creatureType == null) return;

        string words = Keyword.Label(creature);

        Keyword.Fit(creatureType, creature);

        if (words.Length > 0)
        {
            creatureType.text = Keyword.RichLabel(creature);
            creatureType.color = Color.white;
            return;
        }

        creatureType.text = Keyword.TypeOf(creature);
        creatureType.color = plainColor;
    }

    private void ShowDeathrattle(CreatureCard creature)
    {
        int damage = creature != null && creature.hasDeathrattle ? creature.deathrattleDamage : 0;

        Text label = damage > 0 ? DeathrattleLabel() : deathrattleValue;
        if (label == null) return;

        if (label.gameObject.activeSelf != damage > 0) label.gameObject.SetActive(damage > 0);
        if (damage > 0) label.text = damage.ToString();
    }

    private Text DeathrattleLabel()
    {
        if (deathrattleValue != null) return deathrattleValue;
        if (health == null || health.transform.parent == null) return null;

        GameObject made = Instantiate(health.gameObject, health.transform.parent);
        made.name = "DeathrattleValue";
        made.transform.SetSiblingIndex(health.transform.GetSiblingIndex() + 1);

        deathrattleValue = made.GetComponent<Text>();
        deathrattleValue.color = Keyword.Deathrattle;
        deathrattleValue.resizeTextForBestFit = false;
        deathrattleValue.fontSize = deathrattleFontSize;
        deathrattleValue.raycastTarget = false;

        RectTransform rect = made.GetComponent<RectTransform>();
        rect.anchoredPosition = deathrattleCorner;
        rect.sizeDelta = deathrattleSize;

        return deathrattleValue;
    }

    private void ShowBody(bool show)
    {
        if (strength != null && strength.gameObject.activeSelf != show) strength.gameObject.SetActive(show);
        if (health != null && health.gameObject.activeSelf != show) health.gameObject.SetActive(show);
    }

    private void Face(bool up)
    {
        if (cardfront != null) cardfront.color = up ? Color.white : Color.clear;
        if (cardback != null) cardback.color = up ? Color.clear : Color.white;
    }

    private static bool Mine()
    {
        return Player.localPlayer != null && Player.localPlayer.isLocalPlayer;
    }

    private void Update()
    {
        if (cardDragHover == null) return;

        if (playerType != SeatKind.PLAYER || !Mine())
        {
            cardDragHover.canHover = false;
            cardDragHover.canDrag = false;
            ClearOutline();
            return;
        }

        cardDragHover.canHover = true;

        if (Player.gameManager == null || !Player.gameManager.isOurTurn)
        {
            cardDragHover.canDrag = false;
            ClearOutline();
            return;
        }

        Deck deck = Player.localPlayer.deck;

        cardDragHover.canDrag = deck != null && deck.CanPlayCard(cost.text.ToInt());

        if (cardOutline != null) cardOutline.color = cardDragHover.canDrag ? readyColor : Color.clear;
    }
}
