using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HandCardPointer : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Card Properties")]
    public HandCard card;
    public CanvasGroup canvasGroup;

    [Header("Card Hover")]
    public bool canHover = false;

    [Header("Card Drag")]
    public bool canDrag = false;
    public GameObject EmptyCard;

    private const float RestingScale = 0.5f;
    private const float RaisedScale = 0.8f;
    private const float RaisedHeight = 190f;
    private const float DragDepth = 10f;

    private Transform returnTo;
    private GameObject placeholder;
    private Canvas lift;
    private bool hovering;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!canHover || card == null) return;

        hovering = true;

        Lift(RaisedScale, RaisedHeight);
        DrawOnTop(true);

        if (Player.gameManager != null) Player.gameManager.CmdSetHandHover(card.handIndex);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!canHover || card == null) return;

        hovering = false;

        Lift(RestingScale, 0f);
        DrawOnTop(false);

        if (Player.gameManager != null) Player.gameManager.CmdSetHandHover(-1);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!canDrag || EmptyCard == null) return;

        returnTo = transform.parent;
        if (returnTo == null) return;

        placeholder = Instantiate(EmptyCard);
        placeholder.transform.SetParent(returnTo, false);
        placeholder.transform.SetSiblingIndex(transform.GetSiblingIndex());

        transform.SetParent(returnTo.parent, false);

        if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!canDrag) return;

        Camera view = Camera.main;
        if (view == null) return;

        Vector3 point = eventData.position;
        point.z = DragDepth;

        transform.position = view.ScreenToWorldPoint(point);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!canDrag) return;

        if (canvasGroup != null) canvasGroup.blocksRaycasts = true;

        DrawOnTop(false);

        if (returnTo == null || placeholder == null) return;

        transform.SetParent(returnTo, false);
        transform.SetSiblingIndex(placeholder.transform.GetSiblingIndex());

        Destroy(placeholder);
        placeholder = null;
        returnTo = null;
    }

    private void DrawOnTop(bool on)
    {
        if (on && lift == null)
        {
            lift = GetComponent<Canvas>();
            if (lift == null) lift = gameObject.AddComponent<Canvas>();

            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        }

        if (lift == null) return;

        lift.overrideSorting = on;
        lift.sortingOrder = on ? 50 : 0;
    }

    private void Lift(float scale, float height)
    {
        card.transform.localScale = new Vector2(scale, scale);
        card.transform.localPosition = new Vector2(card.transform.localPosition.x, height);
    }

    void LateUpdate()
    {
        if (!hovering || card == null) return;

        if (!canHover)
        {
            hovering = false;
            Lift(RestingScale, 0f);
            DrawOnTop(false);
            return;
        }

        Lift(RaisedScale, RaisedHeight);
    }
}
