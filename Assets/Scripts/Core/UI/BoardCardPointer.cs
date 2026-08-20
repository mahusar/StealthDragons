using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class BoardCardPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public BoardCard card;
    public float hoverDelay = 0.4f;

    private Coroutine waiting;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!Ready()) return;
        if (Player.localPlayer.isTargeting) return;
        if (!Player.gameManager.isOurTurn) return;
        if (card.casterType != Target.FRIENDLIES || !card.CanAttack()) return;

        card.SpawnTargetingArrow(card.card);
        Hide();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (waiting != null) StopCoroutine(waiting);

        waiting = StartCoroutine(RevealAfterDelay());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (waiting != null)
        {
            StopCoroutine(waiting);
            waiting = null;
        }

        Hide();

        if (!Ready()) return;

        Player.gameManager.CmdOnFieldCardHover(gameObject, false, false);
        Player.gameManager.isHoveringField = false;
    }

    private IEnumerator RevealAfterDelay()
    {
        yield return new WaitForSeconds(hoverDelay);

        waiting = null;

        if (!Ready()) yield break;

        if (!Player.localPlayer.isTargeting) Show();

        if (!Player.gameManager.isOurTurn) yield break;

        Player.gameManager.isHoveringField = true;
        Player.gameManager.CmdOnFieldCardHover(gameObject, true, Player.localPlayer.isTargeting);
    }

    private void Show()
    {
        if (card != null && card.cardHover != null) card.cardHover.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (card != null && card.cardHover != null) card.cardHover.gameObject.SetActive(false);
    }

    private bool Ready()
    {
        return card != null && Player.localPlayer != null && Player.gameManager != null;
    }
}
