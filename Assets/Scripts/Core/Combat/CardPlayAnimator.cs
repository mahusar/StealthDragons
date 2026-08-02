using UnityEngine;
using DG.Tweening;

public static class CardPlayAnimator
{
    public static float driveDuration = 0.18f;
    public static float settleDuration = 0.17f;
    public static float entryScaleMultiplier = 1.5f;
    public static float impactScaleMultiplier = 0.93f;
    public static float entryTilt = -6f;
    public static float impactTilt = 2f;

    public static void PlayEntry(Transform card)
    {
        if (card == null) return;

        DOTween.Kill(card, true);

        Vector3 restScale = card.localScale;
        Vector3 restEuler = card.localEulerAngles;
        CanvasGroup fade = card.GetComponent<CanvasGroup>();

        card.localScale = Scaled(restScale, entryScaleMultiplier);
        card.localRotation = Quaternion.Euler(restEuler.x, restEuler.y, entryTilt);
        if (fade != null) fade.alpha = 0f;

        Sequence entry = DOTween.Sequence();
        entry.SetTarget(card);
        entry.SetLink(card.gameObject);

        entry.Append(card.DOScale(Scaled(restScale, impactScaleMultiplier), driveDuration).SetEase(Ease.InQuad));
        entry.Join(card.DOLocalRotate(new Vector3(restEuler.x, restEuler.y, impactTilt), driveDuration).SetEase(Ease.OutQuad));
        if (fade != null)
            entry.Join(fade.DOFade(1f, driveDuration * 0.7f));

        entry.Append(card.DOScale(restScale, settleDuration).SetEase(Ease.OutBack));
        entry.Join(card.DOLocalRotate(restEuler, settleDuration).SetEase(Ease.OutQuad));

        entry.OnComplete(() =>
        {
            if (card == null) return;
            card.localScale = restScale;
            card.localRotation = Quaternion.Euler(restEuler);
            if (fade != null) fade.alpha = 1f;
        });
    }

    private static Vector3 Scaled(Vector3 rest, float multiplier)
    {
        return new Vector3(rest.x * multiplier, rest.y * multiplier, rest.z);
    }
}
