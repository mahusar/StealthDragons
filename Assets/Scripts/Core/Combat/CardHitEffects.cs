using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public static class CardHitEffects
{
    public static float riseDistance = 80f;
    public static float popDuration = 0.75f;
    public static float popScale = 1.4f;
    public static Color damageColor = new Color(1f, 0.27f, 0.22f);
    public static Color healColor = new Color(0.35f, 1f, 0.4f);

    public static float sideOffset = 55f;

    public static float wordShare = 0.45f;

    private static Transform PopupLayer(Transform card)
    {
        Canvas canvas = card.GetComponentInParent<Canvas>();

        return canvas != null ? canvas.transform : card;
    }

    public static void ShowDelta(Text template, Transform card, int delta, float delay, float offsetX)
    {
        if (delta == 0) return;

        Show(template, card, delta > 0 ? "+" + delta : delta.ToString(),
             delta > 0 ? healColor : damageColor, delay, offsetX, 1f);
    }

    public static void ShowWord(Text template, Transform card, string word, Color tint, float delay, float offsetX)
    {
        if (string.IsNullOrEmpty(word)) return;

        Show(template, card, word, tint, delay, offsetX, wordShare);
    }

    private static void Show(Text template, Transform card, string text, Color tint,
                             float delay, float offsetX, float fontShare)
    {
        if (template == null || card == null) return;

        Transform layer = PopupLayer(card);

        GameObject popup = Object.Instantiate(template.gameObject, layer, false);
        popup.name = "DamagePopup";

        popup.transform.position = card.position;

        Vector3 want = template.transform.lossyScale;
        Vector3 host = layer.lossyScale;

        popup.transform.localScale = new Vector3(
            host.x != 0f ? want.x / host.x : 1f,
            host.y != 0f ? want.y / host.y : 1f,
            1f);

        Text label = popup.GetComponent<Text>();
        if (label == null)
        {
            Object.Destroy(popup);
            return;
        }

        label.text = text;
        label.color = tint;
        label.raycastTarget = false;

        if (fontShare != 1f)
        {
            label.resizeTextForBestFit = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = Mathf.Max(1, Mathf.RoundToInt(label.fontSize * fontShare));
        }

        RectTransform rect = popup.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x + offsetX, rect.anchoredPosition.y);
        float startY = rect.anchoredPosition.y;
        Vector3 restScale = rect.localScale;

        CanvasGroup group = popup.GetComponent<CanvasGroup>();
        if (group == null) group = popup.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        Sequence pop = DOTween.Sequence();
        pop.SetLink(popup);
        pop.AppendInterval(delay);
        pop.Append(group.DOFade(1f, popDuration * 0.15f));
        pop.Join(rect.DOAnchorPosY(startY + riseDistance, popDuration).SetEase(Ease.OutCubic));
        pop.Join(rect.DOScale(restScale * popScale, popDuration * 0.2f).SetEase(Ease.OutBack));
        pop.Insert(delay + popDuration * 0.2f, rect.DOScale(restScale, popDuration * 0.25f));
        pop.Insert(delay + popDuration * 0.55f, group.DOFade(0f, popDuration * 0.45f));
        pop.OnComplete(() =>
        {
            if (popup != null) Object.Destroy(popup);
        });
    }
}
