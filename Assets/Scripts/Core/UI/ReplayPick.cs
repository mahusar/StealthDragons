using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class ReplayPick : MonoBehaviour, IPointerClickHandler
{
    private TMP_Text label;

    public System.Action<int> picked;
    public System.Action<int> hovered;

    private int lit;

    void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    void Update()
    {
        if (label == null || hovered == null) return;

        int under = Row(Input.mousePosition);
        if (under == lit) return;

        lit = under;
        hovered(lit);
    }

    private int Row(Vector2 screen)
    {
        Camera view = label.canvas != null && label.canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? label.canvas.worldCamera
            : null;

        int index = TMP_TextUtilities.FindIntersectingLink(label, screen, view);
        if (index < 0) return 0;

        int number;
        return int.TryParse(label.textInfo.linkInfo[index].GetLinkID(), out number) ? number : 0;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (label == null || picked == null || eventData == null) return;

        Camera view = label.canvas != null && label.canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? label.canvas.worldCamera
            : null;

        int number = Row(eventData.position);
        if (number > 0) picked(number);
    }
}
