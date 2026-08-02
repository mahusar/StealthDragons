using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public static class CardDeathAnimator
{
    public static float greyShare = 0.66f;
    public static Color deadTint = new Color(0.32f, 0.32f, 0.36f);

    public static void PlayDeath(Transform card, Image image, CanvasGroup fade, float duration)
    {
        if (card == null || duration <= 0f) return;

        if (image != null) DOTween.Kill(image);

        Sequence death = DOTween.Sequence();
        death.SetTarget(card);
        death.SetLink(card.gameObject);

        if (image != null)
            death.Insert(0f, image.DOColor(deadTint, duration * greyShare).SetEase(Ease.Linear));

        if (fade != null)
            death.Insert(0f, fade.DOFade(0f, duration).SetEase(Ease.InQuad));
    }
}
