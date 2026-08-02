using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class FieldCard : Entity
{
    [SyncVar, HideInInspector] public CardInfo card;

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

    public override void Update()
    {
        base.Update();
        if (image.sprite == null && (card.name != null || cardName.text == ""))
        {
            image.color = Color.white;
            image.sprite = card.image;
            cardName.text = card.name;

            cardHover.UpdateFieldCardInfo(card);
        }

        RefreshHealth();
        strengthText.text = strength.ToString();

        if (dying) return;

        if (CanAttack()) shine.color = readyColor;
        else if (CantAttack()) shine.color = Color.clear;
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
