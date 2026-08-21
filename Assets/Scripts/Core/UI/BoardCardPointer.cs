using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class BoardCardPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IDropHandler
{
    public BoardCard card;
    public float hoverDelay = 0.4f;

    private Coroutine waiting;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ReplayMatch.Active) return;
        if (!Ready()) return;
        if (Player.localPlayer.isTargeting) return;
        if (!Player.gameManager.isOurTurn) return;
        if (card.casterType != Target.FRIENDLIES || !card.CanAttack()) return;

        card.SpawnTargetingArrow(card.card);
        Hide();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (ReplayMatch.Active) return;
        if (!Ready()) return;
        if (eventData == null || eventData.pointerDrag == null) return;

        HandCard dragged = eventData.pointerDrag.GetComponent<HandCard>();
        SpellCard spell = Battlefield.SpellDragged(dragged);

        if (spell == null) return;
        if (!Player.gameManager.isOurTurn) return;
        if (!Player.localPlayer.deck.CanPlayCard(spell.cost)) return;

        if (!spell.targeted)
        {
            Player.gameManager.SetHandHover(-1);
            Player.localPlayer.deck.CmdCastSpell(dragged.handIndex, 0);
            return;
        }

        string trouble;
        if (!Spellbook.Legal(spell, Player.localPlayer, card, out trouble))
        {
            Refuse(trouble);
            return;
        }

        Player.gameManager.SetHandHover(-1);
        Player.localPlayer.deck.CmdCastSpell(dragged.handIndex, card.netId);
    }

    private void Refuse(string trouble)
    {
        Debug.Log("BoardCardPointer: " + trouble + ".");

        if (card == null) return;

        if (trouble.Contains("taunt"))
        {
            card.ShowRefused("TAUNT", Keyword.Taunt);
            ShowGuards();
            return;
        }

        card.ShowRefused("NO", CardHitEffects.damageColor);
    }

    private void ShowGuards()
    {
        Player enemy = card.owner;
        if (enemy == null) return;

        foreach (BoardCard other in FindObjectsByType<BoardCard>(FindObjectsSortMode.None))
        {
            if (other == null || other == card) continue;
            if (other.owner != enemy || other.health <= 0 || !other.taunt) continue;

            other.ShowRefused("HERE", Keyword.Taunt);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (ReplayMatch.Active) return;

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
