using DG.Tweening;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

public static class SpellFlight
{
    public const float Travel = 0.34f;

    public const float Linger = 0.12f;

    public const float Fade = 0.14f;

    public static void Show(Player caster, CardDefinition card, Transform target)
    {
        if (caster == null || card == null || target == null) return;

        BoardCard shown = target.GetComponent<BoardCard>();
        if (shown == null) return;

        BoardCard blank = Prefab();
        if (blank == null) return;

        PlayerPortrait from = PlayerPortrait.For(caster);
        if (from == null || from.portrait == null) return;

        GameObject made = Object.Instantiate(blank.gameObject, target.parent);
        made.name = "SpellFlight";

        Strip(made);
        Paint(made, card);

        RectTransform rect = made.GetComponent<RectTransform>();
        RectTransform like = shown.GetComponent<RectTransform>();

        rect.sizeDelta = like.sizeDelta;
        rect.localScale = like.localScale;
        rect.SetAsLastSibling();
        rect.position = from.portrait.transform.position;

        Vector3 sized = rect.localScale;

        Sequence flight = DOTween.Sequence();
        flight.SetTarget(made.transform);
        flight.SetLink(made);
        flight.Append(made.transform.DOMove(target.position, Travel).SetEase(Ease.OutQuad));
        flight.AppendInterval(Linger);
        flight.Append(made.transform.DOScale(sized * 0.4f, Fade).SetEase(Ease.InQuad));
        flight.OnComplete(() => Object.Destroy(made));
    }

    private static BoardCard Prefab()
    {
        foreach (CardDefinition definition in CardDefinition.Cache.Values)
        {
            CreatureCard creature = definition as CreatureCard;
            if (creature == null || creature.cardPrefab == null) continue;

            return creature.cardPrefab;
        }

        return null;
    }

    private static void Strip(GameObject made)
    {
        foreach (BoardCardPointer pointer in made.GetComponentsInChildren<BoardCardPointer>(true))
            Object.Destroy(pointer);

        foreach (Graphic graphic in made.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    private static void Paint(GameObject made, CardDefinition card)
    {
        BoardCard face = made.GetComponent<BoardCard>();
        if (face == null) return;

        if (face.cardName != null) face.cardName.text = new CardInfo(card).displayName;
        if (face.image != null) { face.image.sprite = card.image; face.image.color = Color.white; }
        if (face.healthText != null) face.healthText.gameObject.SetActive(false);
        if (face.strengthText != null) face.strengthText.gameObject.SetActive(false);
        if (face.keywordFrame != null) face.keywordFrame.enabled = false;
        if (face.cardHover != null) face.cardHover.gameObject.SetActive(false);

        Object.Destroy(face);

        NetworkIdentity identity = made.GetComponent<NetworkIdentity>();
        if (identity != null) Object.Destroy(identity);
    }
}
