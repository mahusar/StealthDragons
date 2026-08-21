using UnityEngine;
using UnityEngine.UI;
using Mirror;
using DG.Tweening;

public class BoardCard : Entity
{
    [SyncVar, HideInInspector] public CardInfo card;

    [HideInInspector] public bool deathrattleSpent;

    [Header("Card Properties")]
    public Image image;
    public Text cardName;
    public Text healthText;
    public Text strengthText;

    [Header("Shine")]
    public Image shine;
    public Color hoverColor;
    public Color readyColor;
    public Color targetColor;

    [Header("Keywords")]
    public Image keywordFrame;
    public Text keywordText;

    [Header("Card Hover")]
    public HandCard cardHover;

    private CardAnimator cardAnimator;
    private int shownHealth;
    private int lastSyncedHealth;
    private bool healthReady;
    private bool sawHealthChange;
    private bool revealPending;
    private float revealAt;
    private bool dying;
    private bool aimed;
    private bool wasAimed;

    public override void Update()
    {
        base.Update();
        if (image.sprite == null && (card.name != null || cardName.text == ""))
        {
            image.color = Color.white;
            image.sprite = card.image;
            cardName.text = card.displayName;

            cardHover.UpdateFieldCardInfo(card);
        }

        RefreshHealth();
        strengthText.text = strength.ToString();
        RefreshKeywordFrame();

        if (dying) return;

        if (aimed)
        {
            shine.color = targetColor;
            return;
        }

        if (wasAimed)
        {
            wasAimed = false;
            shine.color = Color.clear;
        }

        if (CanAttack()) shine.color = readyColor;
        else if (CantAttack()) shine.color = Color.clear;
    }

    public void ShowAimed(bool value)
    {
        aimed = value;

        if (value) wasAimed = true;
    }

    public void ShowRefused(string word, Color tint)
    {
        if (!NetworkClient.active || string.IsNullOrEmpty(word)) return;

        CardHitEffects.ShowWord(healthText, transform, word, tint, 0f, -CardHitEffects.sideOffset);
    }

    public void ShowDeathrattle()
    {
        if (!NetworkClient.active) return;

        CardHitEffects.ShowWord(healthText, transform, "DEATHRATTLE", Keyword.Deathrattle,
                                ImpactDelay(), -CardHitEffects.sideOffset);
    }

    private bool wasShielded;

    private void RefreshKeywordFrame()
    {
        if (keywordFrame == null) return;

        CreatureCard creature = card.Known ? card.data as CreatureCard : null;

        if (creature != null && wasShielded && !shielded) BreakShield();
        wasShielded = shielded;

        if (creature == null || dying)
        {
            if (keywordFrame.enabled) keywordFrame.enabled = false;
            return;
        }

        bool show = Keyword.Any(creature, shielded);
        Color tint = Keyword.Tint(creature, shielded);

        if (keywordFrame.enabled != show) keywordFrame.enabled = show;
        if (show && keywordFrame.color != tint) keywordFrame.color = tint;

        if (keywordText == null) return;

        string words = Keyword.RichLabel(creature);

        Keyword.Fit(keywordText, creature);

        if (keywordText.text != words) keywordText.text = words;
        if (keywordText.color != Color.white) keywordText.color = Color.white;
    }

    private void BreakShield()
    {
        if (!NetworkClient.active || keywordFrame == null) return;

        keywordFrame.color = Keyword.Shield;
        keywordFrame.enabled = true;

        DG.Tweening.Sequence shatter = DG.Tweening.DOTween.Sequence();
        shatter.SetLink(keywordFrame.gameObject);
        shatter.Append(keywordFrame.transform.DOScale(keywordFrame.transform.localScale * 1.25f, 0.12f));
        shatter.Join(keywordFrame.DOFade(0f, 0.22f));
        shatter.Append(keywordFrame.transform.DOScale(Vector3.one, 0.01f));
        shatter.OnComplete(() =>
        {
            if (keywordFrame == null) return;
            Color back = keywordFrame.color;
            back.a = 1f;
            keywordFrame.color = back;
        });
    }

    private void RefreshHealth()
    {
        if (!healthReady)
        {
            shownHealth = health;
            lastSyncedHealth = health;
            healthReady = true;
        }
        else if (health != lastSyncedHealth)
        {
            int delta = health - lastSyncedHealth;
            lastSyncedHealth = health;
            sawHealthChange = true;
            revealAt = Time.time + ImpactDelay();
            revealPending = true;

            if (NetworkClient.active)
            {
                CardAnimator animator = Animator();
                float offsetX = animator != null && animator.isAttacking
                    ? -CardHitEffects.sideOffset
                    : CardHitEffects.sideOffset;

                CardHitEffects.ShowDelta(healthText, transform, delta, ImpactDelay(), offsetX);
            }
        }

        if (revealPending && Time.time >= revealAt)
        {
            shownHealth = lastSyncedHealth;
            revealPending = false;
        }

        healthText.text = shownHealth.ToString();

        if (!dying && sawHealthChange && !revealPending && shownHealth <= 0)
        {
            dying = true;
            if (NetworkClient.active)
                CardDeathAnimator.PlayDeath(transform, image, GetComponent<CanvasGroup>(), DeathDuration());
        }
    }

    private CardAnimator Animator()
    {
        if (cardAnimator == null) cardAnimator = GetComponent<CardAnimator>();
        return cardAnimator;
    }

    private float ImpactDelay()
    {
        CardAnimator animator = Animator();
        return animator != null ? animator.moveDuration : 0.5f;
    }

    private float DeathDuration()
    {
        CardAnimator animator = Animator();
        return animator != null ? animator.attackPause + animator.returnDuration : 0.5f;
    }

    [Server]
    public void ServerBeginTurn()
    {
        if (waitTurn > 0) waitTurn--;
        hasAttackedThisTurn = false;
    }
}
