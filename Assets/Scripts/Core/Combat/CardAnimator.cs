using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class CardAnimator : MonoBehaviour
{
    public float moveDuration = 0.5f;
    public float returnDuration = 0.3f;
    public float attackPause = 0.2f;

    public void AnimateAttack(Transform attacker, Transform target, System.Action onComplete)
    {
        DOTween.Kill(attacker, true);

        Vector3 originalPos = attacker.position;

        Sequence attackSequence = DOTween.Sequence();
        attackSequence.SetTarget(attacker);
        attackSequence.SetLink(attacker.gameObject);
        attackSequence.Append(attacker.DOMove(target.position, moveDuration).SetEase(Ease.InOutSine));
        attackSequence.AppendInterval(attackPause);
        attackSequence.Append(attacker.DOMove(originalPos, returnDuration).SetEase(Ease.InOutSine));
        attackSequence.OnComplete(() =>
        {
            RestoreLayout(attacker);
            onComplete?.Invoke();
        });
    }

    private void RestoreLayout(Transform card)
    {
        if (card == null) return;
        if (card.parent is RectTransform parent)
            LayoutRebuilder.MarkLayoutForRebuild(parent);
    }
}
