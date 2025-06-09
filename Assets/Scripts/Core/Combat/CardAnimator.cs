using UnityEngine;
using DG.Tweening;

public class CardAnimator : MonoBehaviour
{
    public float moveDuration = 0.5f;
    public float returnDuration = 0.3f;
    public float attackPause = 0.2f;

    public void AnimateAttack(Transform attacker, Transform target, System.Action onComplete)
    {
        Vector3 originalPos = attacker.position;

        Sequence attackSequence = DOTween.Sequence();
        attackSequence.Append(attacker.DOMove(target.position, moveDuration).SetEase(Ease.InOutSine));
        attackSequence.AppendInterval(attackPause); // Pause at target
        attackSequence.Append(attacker.DOMove(originalPos, returnDuration).SetEase(Ease.InOutSine));
        attackSequence.OnComplete(() => onComplete?.Invoke());
    }
}