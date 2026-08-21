using UnityEngine;
using UnityEngine.EventSystems;

public class Battlefield : MonoBehaviour, IDropHandler
{
    public Transform content;

    public void OnDrop(PointerEventData eventData)
    {
        if (ReplayMatch.Active) return;
        if (eventData == null || eventData.pointerDrag == null) return;

        HandCard card = eventData.pointerDrag.GetComponent<HandCard>();
        if (card == null) return;

        Player player = Player.localPlayer;
        if (player == null || player.deck == null) return;

        if (!player.IsOurTurn()) return;
        if (!player.deck.CanPlayCard(card.cost.text.ToInt())) return;

        SpellCard spell = SpellDragged(card);

        if (spell != null && spell.targeted)
        {
            if (Player.gameManager != null) Player.gameManager.SetHandHover(-1);

            player.SpawnTargetingArrow(player.deck.hand[card.handIndex], false, card.handIndex);
            return;
        }

        if (Player.gameManager != null) Player.gameManager.SetHandHover(-1);

        if (spell != null)
        {
            player.deck.CmdCastSpell(card.handIndex, 0);
            return;
        }

        player.deck.CmdPlayCard(card.handIndex, SlotUnder(eventData));
    }

    public static SpellCard SpellDragged(HandCard card)
    {
        if (card == null) return null;

        Player player = Player.localPlayer;
        if (player == null || player.deck == null) return null;
        if (card.handIndex < 0 || card.handIndex >= player.deck.hand.Count) return null;

        CardInfo held = player.deck.hand[card.handIndex];

        return held.Known ? held.data as SpellCard : null;
    }

    public int SlotUnder(PointerEventData eventData)
    {
        Camera view = eventData != null && eventData.pressEventCamera != null
            ? eventData.pressEventCamera
            : EventCamera();

        return SlotAt(eventData != null ? eventData.position.x : 0f, view);
    }

    public int SlotAt(float screenX, Camera view)
    {
        if (content == null) return -1;

        int slot = 0;

        for (int i = 0; i < content.childCount; i++)
        {
            Transform child = content.GetChild(i);
            if (child == null || !child.gameObject.activeSelf) continue;

            if (screenX < RectTransformUtility.WorldToScreenPoint(view, child.position).x) break;

            slot++;
        }

        return slot;
    }

    private Camera EventCamera()
    {
        Canvas canvas = GetComponentInParent<Canvas>();

        if (canvas == null) return null;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }
}
