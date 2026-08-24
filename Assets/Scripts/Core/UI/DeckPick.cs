using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class DeckPick : MonoBehaviour, IPointerClickHandler
{
    private TMP_Text label;

    public System.Action<string> added;
    public System.Action<string> removed;
    public System.Action<string> hovered;

    private string lit = "";

    void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    void Update()
    {
        if (label == null || hovered == null) return;

        string under = Row(Input.mousePosition);
        if (under == lit) return;

        lit = under;
        hovered(lit);
    }

    private string Row(Vector2 screen)
    {
        if (label == null) return "";

        Camera view = label.canvas != null && label.canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? label.canvas.worldCamera
            : null;

        int index = TMP_TextUtilities.FindIntersectingLink(label, screen, view);
        if (index < 0) return "";

        return label.textInfo.linkInfo[index].GetLinkID();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null) return;

        string cardId = Row(eventData.position);
        if (cardId.Length == 0) return;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (removed != null) removed(cardId);
            return;
        }

        if (added != null) added(cardId);
    }
}
