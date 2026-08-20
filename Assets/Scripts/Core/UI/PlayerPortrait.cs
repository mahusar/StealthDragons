using UnityEngine;
using UnityEngine.UI;

public partial class PlayerPortrait : MonoBehaviour
{
    public GameObject panel;
    public Image portrait;
    public Text username;
    public Text deckAmount;
    public Text graveyardAmount;
    public Text handAmount;
    public Text health;
    public Text mana;
    public SeatKind playerType;

    [Header("Aim")]
    public Image aimGlow;

    private static readonly Color AimColor = new Color(0.98f, 0.02f, 0f, 0.28f);

    private const float AimEdge = 3f;

    private PlayerInfo enemyInfo;

    private int shownHealth;
    private bool healthKnown;

    void Update()
    {
        Player me = Player.localPlayer;

        if (me == null)
        {
            Hide();
            return;
        }

        if (me.hasEnemy && me.enemyInfo.player != null) enemyInfo = me.enemyInfo;

        if (playerType == SeatKind.PLAYER)
        {
            ShowMine(me);
            return;
        }

        if (playerType == SeatKind.ENEMY && me.hasEnemy && Seated(enemyInfo))
        {
            ShowTheirs(enemyInfo);
            return;
        }

        Hide();
    }

    private void ShowMine(Player me)
    {
        if (me.deck == null)
        {
            Hide();
            return;
        }

        Show();

        me.transform.position = portrait.transform.position;
        me.spawnOffset = portrait.transform;

        ShowHealthChange(me.health);

        portrait.sprite = me.portrait;
        username.text = me.username;
        deckAmount.text = me.deck.deckList.Count.ToString();
        graveyardAmount.text = me.deck.graveyard.Count.ToString();
        handAmount.text = me.deck.hand.Count.ToString();
        health.text = me.health.ToString();
        mana.text = me.mana.ToString();
    }

    private void ShowTheirs(PlayerInfo them)
    {
        Show();

        them.player.transform.position = portrait.transform.position;
        if (them.data != null) them.data.spawnOffset = portrait.transform;

        ShowHealthChange(them.health);

        portrait.sprite = them.portrait;
        username.text = them.username;
        deckAmount.text = them.deckCount.ToString();
        graveyardAmount.text = them.graveCount.ToString();
        handAmount.text = them.handCount.ToString();
        health.text = them.health.ToString();
        mana.text = them.mana.ToString();
    }

    private void ShowHealthChange(int now)
    {
        if (!healthKnown)
        {
            shownHealth = now;
            healthKnown = true;
            return;
        }

        if (now == shownHealth) return;

        int delta = now - shownHealth;
        shownHealth = now;

        if (health == null || portrait == null) return;

        CardHitEffects.ShowDelta(health, portrait.transform, delta, 0f, 0f);
    }

    public static PlayerPortrait For(Player player)
    {
        if (player == null) return null;

        SeatKind wanted = player == Player.localPlayer ? SeatKind.PLAYER : SeatKind.ENEMY;

        foreach (PlayerPortrait found in FindObjectsByType<PlayerPortrait>(FindObjectsSortMode.None))
            if (found != null && found.playerType == wanted) return found;

        return null;
    }

    public void ShowAimed(bool aimed)
    {
        Image glow = Glow();
        if (glow == null) return;

        glow.color = aimed ? AimColor : Color.clear;
    }

    private Image Glow()
    {
        if (aimGlow != null) return aimGlow;
        if (portrait == null || portrait.transform.parent == null) return null;

        GameObject made = new GameObject("AimGlow", typeof(RectTransform));

        RectTransform rect = made.GetComponent<RectTransform>();
        rect.SetParent(portrait.transform.parent, false);
        rect.SetSiblingIndex(0);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-AimEdge, -AimEdge);
        rect.offsetMax = new Vector2(AimEdge, AimEdge);

        aimGlow = made.AddComponent<Image>();
        aimGlow.raycastTarget = false;
        aimGlow.color = Color.clear;

        return aimGlow;
    }

    private static bool Seated(PlayerInfo info)
    {
        return info.player != null && info.player.gameObject.activeInHierarchy;
    }

    private void Show()
    {
        if (panel != null && !panel.activeSelf) panel.SetActive(true);
    }

    private void Hide()
    {
        if (panel != null && panel.activeSelf) panel.SetActive(false);
    }
}
