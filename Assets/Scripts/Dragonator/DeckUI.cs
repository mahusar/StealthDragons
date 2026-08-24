using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DeckUI : MonoBehaviour
{
    [Tooltip("Left empty it is cloned from the replay button, so the menu needs no hand wiring.")]
    [SerializeField] private Button deckButton;

    [Tooltip("Left empty it is cloned from the replay panel, which brings its own styling.")]
    [SerializeField] private GameObject deckPanel;

    [SerializeField] private string deckButtonLabel = "DECK";

    [Tooltip("Where the deck button sits among the menu buttons.")]
    [SerializeField] private int deckButtonRow = 2;

    [Tooltip("Where START OVER sits, matching the replay panel's own tidy-up button.")]
    [SerializeField] private Vector2 resetCorner = new Vector2(0f, -266f);

    [Tooltip("The status text is shortened by this much to make room for START OVER.")]
    [SerializeField] private float resetRoom = 38f;

    [Tooltip("How far from the pointer the pointed-at card is drawn.")]
    [SerializeField] private Vector2 previewOffset = new Vector2(230f, 0f);

    [Tooltip("How big the pointed-at card is drawn.")]
    [SerializeField] private float previewScale = 0.62f;

    private ReplayUI replays;

    private Connect connect;

    private DeckDraft draft;

    private TMP_Text status;
    private TMP_Text details;
    private TMP_InputField typed;
    private Button saveButton;
    private Button closeButton;
    private Button resetButton;

    private string lit = "";

    private string filter = "";

    private HandCard preview;

    private RectTransform previewRect;

    private Canvas host;

    private bool Open
    {
        get { return deckPanel != null && deckPanel.activeSelf; }
    }

    void Start()
    {
        replays = GetComponent<ReplayUI>();
        connect = GetComponent<Connect>();

        Build();

        if (deckPanel != null) deckPanel.SetActive(false);
    }

    private void Build()
    {
        if (deckButton == null) deckButton = CloneButton();

        if (deckButton != null)
        {
            deckButton.onClick.RemoveAllListeners();
            deckButton.onClick.AddListener(OnDeckClicked);
            Label(deckButton, deckButtonLabel);
        }

        if (deckPanel == null) deckPanel = ClonePanel();

        if (deckPanel != null) Adopt(deckPanel);
    }

    private Button CloneButton()
    {
        Button from = replays != null ? replays.ReplayButton() : null;
        if (from == null) return null;

        Transform parent = from.transform.parent;
        if (parent == null) return null;

        GameObject made = Instantiate(from.gameObject, parent);
        made.name = "DeckButton";
        made.transform.SetSiblingIndex(Mathf.Clamp(deckButtonRow, 0, parent.childCount - 1));

        Button button = made.GetComponent<Button>();
        button.onClick.RemoveAllListeners();

        return button;
    }

    private GameObject ClonePanel()
    {
        GameObject from = replays != null ? replays.ReplayPanel() : null;
        if (from == null) return null;

        bool was = from.activeSelf;
        from.SetActive(false);

        GameObject made = Instantiate(from, from.transform.parent);
        made.name = "DeckPanel";
        made.transform.SetSiblingIndex(from.transform.GetSiblingIndex() + 1);

        from.SetActive(was);

        return made;
    }

    private void Adopt(GameObject panel)
    {
        foreach (Transform child in panel.transform)
        {
            string name = child.name;

            if (name.Contains("ResultText")) status = child.GetComponent<TMP_Text>();
            else if (name.Contains("DetailsText")) details = child.GetComponent<TMP_Text>();
            else if (name.Contains("AddressInput")) typed = child.GetComponent<TMP_InputField>();
            else if (name.Contains("SubmitButton")) saveButton = child.GetComponent<Button>();
            else if (name.Contains("PruneButton") || name.Contains("ResetButton")) resetButton = child.GetComponent<Button>();
            else if (name.Contains("CloseButton")) closeButton = child.GetComponent<Button>();
        }

        if (details != null) details.text = Blurb();

        if (resetButton == null) resetButton = MakeResetButton();

        if (saveButton != null)
        {
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(OnSaveClicked);
            Label(saveButton, "SAVE");
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(OnCloseClicked);
            Label(closeButton, "CLOSE");
        }

        if (resetButton != null)
        {
            resetButton.onClick.RemoveAllListeners();
            resetButton.onClick.AddListener(OnResetClicked);
            Label(resetButton, "START OVER");
            resetButton.gameObject.SetActive(true);
        }

        if (typed != null)
        {
            typed.onValueChanged.RemoveAllListeners();
            typed.onSubmit.RemoveAllListeners();
            typed.onValueChanged.AddListener(OnFilterChanged);
            typed.onSubmit.AddListener(OnFilterSubmitted);

            TMP_Text hint = typed.placeholder as TMP_Text;
            if (hint != null) hint.text = "find a card by name";
        }
    }

    public void CloseIfOpen()
    {
        if (Open) OnCloseClicked();
    }

#if UNITY_EDITOR
    [ContextMenu("Build In Scene")]
    private void BuildInScene()
    {
        replays = GetComponent<ReplayUI>();
        connect = GetComponent<Connect>();

        if (replays == null)
        {
            Debug.LogWarning("DeckUI: there is no ReplayUI to clone the deck button and panel from.");
            return;
        }

        Build();

        if (deckPanel != null) deckPanel.SetActive(false);

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);

        Debug.Log("DeckUI: the deck button and panel are now real scene objects, so they survive without cloning.");
    }
#endif

    private Button MakeResetButton()
    {
        if (closeButton == null) return null;

        Transform parent = closeButton.transform.parent;
        if (parent == null) return null;

        GameObject made = Instantiate(closeButton.gameObject, parent);
        made.name = "DeckResetButton";
        made.transform.SetSiblingIndex(closeButton.transform.GetSiblingIndex());

        RectTransform rect = made.GetComponent<RectTransform>();
        RectTransform from = closeButton.GetComponent<RectTransform>();

        rect.anchorMin = from.anchorMin;
        rect.anchorMax = from.anchorMax;
        rect.pivot = from.pivot;
        rect.sizeDelta = from.sizeDelta;
        rect.anchoredPosition = resetCorner;

        MakeRoom();

        Button button = made.GetComponent<Button>();
        button.onClick.RemoveAllListeners();

        return button;
    }

    private void MakeRoom()
    {
        if (status == null || resetRoom <= 0f) return;

        RectTransform rect = status.GetComponent<RectTransform>();
        if (rect == null) return;

        rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y - resetRoom);
    }

    private static string Blurb()
    {
        string line = ((char)10).ToString();

        return "Build the deck you play with." + line +
               "<size=13>" + Decklist.Size + " cards, at most " + Decklist.MaxCopies + " of each." + line +
               "It starts full - right-click a card on the left to take one out, then click another in.</size>";
    }

    private void OnDeckClicked()
    {
        if (Open)
        {
            OnCloseClicked();
            return;
        }

        if (deckPanel == null)
        {
            Debug.LogWarning("DeckUI: there is no deck panel to open.");
            return;
        }

        if (replays != null) replays.CloseIfOpen();

        draft = DeckDraft.From(Starting());

        deckPanel.SetActive(true);

        DeckPick pick = connect != null ? connect.OpenDeckList() : null;

        if (pick != null)
        {
            pick.added = OnAdd;
            pick.removed = OnTake;
            pick.hovered = OnHover;
        }

        Redraw();
        Say(Hint());
    }

    private string Starting()
    {
        string saved = Decklist.Load();

        if (!string.IsNullOrEmpty(saved) && Decklist.Legal(saved)) return saved;

        return Default();
    }

    private static string Default()
    {
        XSTDragonNetworkManager manager = XSTDragonNetworkManager.singleton as XSTDragonNetworkManager;

        if (manager == null) manager = FindAnyObjectByType<XSTDragonNetworkManager>();

        GameObject prefab = manager != null ? manager.playerPrefab : null;
        Deck deck = prefab != null ? prefab.GetComponent<Deck>() : null;

        if (deck == null) return "";

        return Decklist.Encode(deck.startingDeck);
    }

    private void OnAdd(string cardId)
    {
        if (draft == null) return;

        string trouble;

        if (!draft.Add(cardId, out trouble))
        {
            Redraw();
            Say(Named(cardId) + " was not added - " + trouble + ".");
            return;
        }

        Redraw();
        Say("Added " + Named(cardId) + ".");
    }

    private void OnTake(string cardId)
    {
        if (draft == null) return;

        if (!draft.Remove(cardId))
        {
            Say("There is no " + Named(cardId) + " in the deck to take back.");
            return;
        }

        Redraw();
        Say("Took back " + Named(cardId) + ".");
    }

    private void OnHover(string cardId)
    {
        if (draft == null || cardId == lit) return;

        lit = cardId;

        ShowPreview(cardId);

        Redraw();
    }

    void Update()
    {
        if (preview == null || !preview.gameObject.activeSelf) return;

        PlacePreview();
    }

    private void ShowPreview(string cardId)
    {
        CardDefinition card = Pooled(cardId);

        if (card == null || !Open)
        {
            HidePreview();
            return;
        }

        if (!EnsurePreview()) return;

        preview.gameObject.SetActive(true);
        preview.UpdateFieldCardInfo(new CardInfo(card));
        previewRect.SetAsLastSibling();

        PlacePreview();
    }

    private void HidePreview()
    {
        if (preview != null) preview.gameObject.SetActive(false);
    }

    private bool EnsurePreview()
    {
        if (preview != null) return true;

        HandCard blank = PreviewPrefab();

        if (blank == null)
        {
            Debug.LogWarning("DeckUI: no card face was found in the pool, so the pointed-at card cannot be shown.");
            return false;
        }

        Canvas canvas = deckPanel != null ? deckPanel.GetComponentInParent<Canvas>() : null;

        if (canvas == null)
        {
            Debug.LogWarning("DeckUI: the deck panel is on no canvas, so the pointed-at card cannot be shown.");
            return false;
        }

        host = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;

        GameObject made = Instantiate(blank.gameObject, host.transform);
        made.name = "DeckPreview";
        made.SetActive(true);

        preview = made.GetComponent<HandCard>();
        previewRect = made.GetComponent<RectTransform>();

        if (preview == null || previewRect == null)
        {
            Debug.LogWarning("DeckUI: the card face clone came out without a face or a rect.");
            Destroy(made);
            return false;
        }

        foreach (HandCardPointer pointer in made.GetComponentsInChildren<HandCardPointer>(true))
            Destroy(pointer);

        foreach (Graphic graphic in made.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        previewRect.anchorMin = new Vector2(0.5f, 0.5f);
        previewRect.anchorMax = new Vector2(0.5f, 0.5f);
        previewRect.pivot = new Vector2(0.5f, 0.5f);
        previewRect.localScale = new Vector3(previewScale, previewScale, 1f);

        return true;
    }

    private static HandCard PreviewPrefab()
    {
        foreach (CardDefinition card in Decklist.Pool())
        {
            CreatureCard creature = card as CreatureCard;

            if (creature == null || creature.cardPrefab == null) continue;
            if (creature.cardPrefab.cardHover == null) continue;

            return creature.cardPrefab.cardHover;
        }

        return null;
    }

    private void PlacePreview()
    {
        RectTransform canvasRect = host != null ? host.transform as RectTransform : null;

        if (canvasRect == null || previewRect == null) return;

        Camera view = host.renderMode == RenderMode.ScreenSpaceOverlay ? null : host.worldCamera;

        Vector2 local;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, view, out local)) return;

        Vector2 half = previewRect.sizeDelta * previewScale * 0.5f;
        Vector2 room = canvasRect.rect.size * 0.5f;

        float x = local.x + previewOffset.x;

        if (x + half.x > room.x) x = local.x - previewOffset.x;

        previewRect.anchoredPosition = new Vector2(
            Mathf.Clamp(x, -room.x + half.x, room.x - half.x),
            Mathf.Clamp(local.y + previewOffset.y, -room.y + half.y, room.y - half.y));
    }

    private static CardDefinition Pooled(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return null;

        foreach (CardDefinition card in Decklist.Pool())
            if (card.CardID == cardId) return card;

        return null;
    }

    private void OnFilterChanged(string text)
    {
        filter = text != null ? text : "";

        Redraw();
    }

    private void OnFilterSubmitted(string text)
    {
        if (draft == null) return;

        string only = OnlyMatch(text);

        if (only.Length == 0)
        {
            Say("Narrow that down to one card, then press Enter to add it.");
            return;
        }

        OnAdd(only);
    }

    private static string OnlyMatch(string text)
    {
        string wanted = text != null ? text.Trim().ToLowerInvariant() : "";
        if (wanted.Length == 0) return "";

        string found = "";

        foreach (CardDefinition card in Decklist.Pool())
        {
            if (CardInfo.Pretty(card.name).ToLowerInvariant().IndexOf(wanted) < 0) continue;

            if (found.Length > 0) return "";

            found = card.CardID;
        }

        return found;
    }

    private void OnResetClicked()
    {
        if (draft == null) return;

        draft = DeckDraft.From(Default());

        Redraw();
        Say("Back to the starting deck. Nothing is saved until you press SAVE.");
    }

    private void OnSaveClicked()
    {
        if (draft == null) return;

        string wire = draft.Wire();

        if (!Decklist.Legal(wire))
        {
            Say("This deck cannot be saved yet - " + draft.Trouble + ".");
            return;
        }

        Decklist.Save(Decklist.Canonical(wire));

        Say("Saved. Your next match is played with this deck.");
    }

    private void OnCloseClicked()
    {
        if (deckPanel != null) deckPanel.SetActive(false);
        if (connect != null) connect.CloseSideList();

        HidePreview();

        draft = null;
        lit = "";
        filter = "";

        if (typed != null) typed.text = "";
    }

    private void Redraw()
    {
        if (connect == null || draft == null) return;

        connect.SetSideText(draft.Describe(lit, filter));
    }

    private string Hint()
    {
        string saved = Decklist.Load();

        return string.IsNullOrEmpty(saved)
            ? "You are playing the starting deck."
            : "Your saved deck is loaded.";
    }

    private static string Named(string cardId)
    {
        foreach (CardDefinition card in Decklist.Pool())
            if (card.CardID == cardId) return CardInfo.Pretty(card.name);

        return "card " + cardId;
    }

    private void Say(string message)
    {
        if (status != null) status.text = message;
    }

    private static void Label(Button button, string text)
    {
        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);

        if (tmp != null)
        {
            tmp.text = text;
            return;
        }

        Text plain = button.GetComponentInChildren<Text>(true);
        if (plain != null) plain.text = text;
    }
}
