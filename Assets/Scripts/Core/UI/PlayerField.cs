using UnityEngine.EventSystems;
using UnityEngine;

public class PlayerField : MonoBehaviour, IDropHandler
{
    public Transform content;

    public void OnDrop(PointerEventData eventData)
    {
        HandCard card = eventData.pointerDrag.transform.GetComponent<HandCard>();
        Player player = Player.localPlayer;
        int manaCost = card.cost.text.ToInt();

        if (player.IsOurTurn() && player.deck.CanPlayCard(manaCost))
        {
            int index = card.handIndex;

            Player.gameManager.isSpawning = true;
            Player.gameManager.isHovering = false;
            Player.gameManager.CmdOnCardHover(0, index);
            player.deck.CmdPlayCard(index);
        }
    }

}
