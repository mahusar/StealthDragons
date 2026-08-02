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

    public static void ShowDelta(Text template, Transform card, int delta, float delay, float offsetX)
    {
        if (template == null || card == null || delta == 0) return;

        GameObject popup = Object.Instantiate(template.gameObject, card, false);
        popup.name = "DamagePopup";

        Text label = popup.GetComponent<Text>();
        if (label == null)
        {
            Object.Destroy(popup);
            return;
        }

        label.text = delta > 0 ? "+" + delta : delta.ToString();
        label.color = delta > 0 ? healColor : damageColor;
        label.raycastTarget = false;

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
