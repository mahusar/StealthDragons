using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class PlayerPortrait : MonoBehaviour, IPointerClickHandler
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

    private static readonly Color ReadyColor = new Color(0.25f, 0.6f, 1f, 0.28f);

    private const float AimEdge = 3f;

    private bool aimed;

    private bool powerReady;

    [Header("Ready Ring")]
    [Tooltip("Segments walked around the portrait edge. The travelling highlight is smoother the more there are.")]
    public int ringSegments = 32;
    public float ringThickness = 3f;
    [Tooltip("Laps per second.")]
    public float ringSpeed = 0.32f;
    [Tooltip("Higher makes the bright spot tighter.")]
    public float ringSharpness = 4f;
    [Tooltip("Alpha of the dimmest part - keep it near the old solid glow so the border always reads.")]
    public float ringFloor = 0.22f;
    public float ringPeak = 0.9f;

    private Image[] ring;

    private float[] ringPhase;

    [Header("Hero Power Strike")]
    [Tooltip("How the portrait lunges at what the hero power hits, matching a card's attack.")]
    public float strikeOut = 0.28f;
    public float strikeHold = 0.1f;
    public float strikeBack = 0.22f;
    [Tooltip("How big the portrait swells while it strikes, so the hero reads as the attacker.")]
    public float strikeSwell = 1.6f;

    private CardAnimator strikeAnimator;


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

    private void LateUpdate()
    {
        PaintRing();
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

        ShowPowerReady(HeroPower.Ready(me) && Player.gameManager != null && Player.gameManager.isOurTurn);
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

        Player theirs = them.data;

        ShowPowerReady(HeroPower.Has(theirs) && !HeroPower.Spent(theirs));
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

    public void OnPointerClick(PointerEventData eventData)
    {
        if (playerType != SeatKind.PLAYER) return;

        Player me = Player.localPlayer;
        if (me == null || me.deck == null) return;

        if (Player.gameManager == null || !Player.gameManager.isOurTurn) return;

        if (me.isTargeting)
        {
            me.DestroyTargetingArrow();
            return;
        }

        if (!HeroPower.Has(me)) return;

        if (HeroPower.Spent(me))
        {
            Debug.Log("PlayerPortrait: the hero power has already been used this turn.");
            return;
        }

        if (!HeroPower.Affordable(me))
        {
            Debug.Log("PlayerPortrait: the hero power costs " + HeroPower.CostOf(me) + ".");
            return;
        }

        SpellCard power = HeroPower.Of(me);

        if (!HeroPower.Targets(me))
        {
            me.deck.CmdUseHeroPower(0);
            return;
        }

        if (Spellbook.Preview(power, me).Count == 0)
        {
            Debug.Log("PlayerPortrait: the hero power has nothing it can hit right now.");
            return;
        }

        me.SpawnTargetingArrow(new CardInfo(power), true);
        AimHighlight.Show(power, me, true);
    }

    public static PlayerPortrait For(Player player)
    {
        if (player == null) return null;

        SeatKind wanted = player == Player.localPlayer ? SeatKind.PLAYER : SeatKind.ENEMY;

        foreach (PlayerPortrait found in FindObjectsByType<PlayerPortrait>(FindObjectsSortMode.None))
            if (found != null && found.playerType == wanted) return found;

        return null;
    }

    public void ShowAimed(bool on)
    {
        aimed = on;
        PaintGlow();
    }

    private void ShowPowerReady(bool on)
    {
        if (powerReady == on) return;

        powerReady = on;
        PaintGlow();
    }

    private void PaintGlow()
    {
        Image glow = Glow();
        if (glow == null) return;

        glow.color = aimed ? AimColor : Color.clear;
    }

    public void AnimateStrike(Transform target)
    {
        if (portrait == null) return;

        CardAnimator animator = Animator();
        if (animator == null) return;

        if (target == null)
        {
            Debug.Log("PlayerPortrait: the hero power went off with nothing to lunge at.");
            return;
        }

        animator.AnimateAttack(portrait.transform, target, null);
        Swell();
    }

    private void Swell()
    {
        if (strikeSwell <= 1f) return;

        Transform swelling = portrait.transform;

        Vector3 home = Vector3.one;

        Sequence punch = DOTween.Sequence();
        punch.SetTarget(swelling);
        punch.SetLink(swelling.gameObject);
        punch.Append(swelling.DOScale(home * strikeSwell, strikeOut).SetEase(Ease.OutQuad));
        punch.AppendInterval(strikeHold);
        punch.Append(swelling.DOScale(home, strikeBack).SetEase(Ease.InQuad));
        punch.OnComplete(() => swelling.localScale = home);
    }

    private CardAnimator Animator()
    {
        if (strikeAnimator != null) return strikeAnimator;

        strikeAnimator = GetComponent<CardAnimator>();
        if (strikeAnimator == null) strikeAnimator = gameObject.AddComponent<CardAnimator>();

        strikeAnimator.moveDuration = strikeOut;
        strikeAnimator.attackPause = strikeHold;
        strikeAnimator.returnDuration = strikeBack;

        return strikeAnimator;
    }

    private void PaintRing()
    {
        bool on = powerReady && !aimed;

        if (!on && ring == null) return;
        if (on) BuildRing();
        if (ring == null) return;

        float lap = Time.unscaledTime * ringSpeed;

        for (int i = 0; i < ring.Length; i++)
        {
            if (ring[i] == null) continue;

            if (!on)
            {
                ring[i].color = Color.clear;
                continue;
            }

            float wave = 0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * (ringPhase[i] - lap));
            float alpha = Mathf.Lerp(ringFloor, ringPeak, Mathf.Pow(wave, ringSharpness));

            ring[i].color = new Color(ReadyColor.r, ReadyColor.g, ReadyColor.b, alpha);
        }
    }

    private void BuildRing()
    {
        if (ring != null) return;
        if (portrait == null || portrait.transform.parent == null) return;

        int perEdge = Mathf.Max(1, ringSegments / 4);
        int count = perEdge * 4;

        ring = new Image[count];
        ringPhase = new float[count];

        Rect area = portrait.GetComponent<RectTransform>().rect;
        float w = area.width + ringThickness * 2f;
        float h = area.height + ringThickness * 2f;

        int at = 0;

        for (int edge = 0; edge < 4; edge++)
        {
            for (int i = 0; i < perEdge; i++)
            {
                float along = (i + 0.5f) / perEdge;

                Vector2 pos;
                Vector2 size;

                if (edge == 0)
                {
                    pos = new Vector2(-w * 0.5f + along * w, h * 0.5f);
                    size = new Vector2(w / perEdge, ringThickness);
                }
                else if (edge == 1)
                {
                    pos = new Vector2(w * 0.5f, h * 0.5f - along * h);
                    size = new Vector2(ringThickness, h / perEdge);
                }
                else if (edge == 2)
                {
                    pos = new Vector2(w * 0.5f - along * w, -h * 0.5f);
                    size = new Vector2(w / perEdge, ringThickness);
                }
                else
                {
                    pos = new Vector2(-w * 0.5f, -h * 0.5f + along * h);
                    size = new Vector2(ringThickness, h / perEdge);
                }

                GameObject made = new GameObject("ReadyRing", typeof(RectTransform));

                RectTransform rect = made.GetComponent<RectTransform>();
                rect.SetParent(portrait.transform.parent, false);
                rect.SetSiblingIndex(0);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = pos;
                rect.sizeDelta = size;

                Image piece = made.AddComponent<Image>();
                piece.raycastTarget = false;
                piece.color = Color.clear;

                ring[at] = piece;
                ringPhase[at] = (edge + along) * 0.25f;
                at++;
            }
        }
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
