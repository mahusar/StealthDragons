using UnityEngine;

public class ArrowHead : MonoBehaviour
{
    [Header("Arrow Heads")]
    public Sprite defaultHead;
    public Sprite targetHead;

    [Header("Properties")]
    public SpriteRenderer spriteRenderer;
    public LayerMask targetLayer;

    [HideInInspector] public CardInfo card;

    public void FindTargets(Entity caster, Vector2 mousePos, bool IsAbility)
    {
        RaycastHit2D[] hitInfo = Physics2D.CircleCastAll(mousePos, 0.1f, Vector2.zero, 1f, targetLayer);

        if (hitInfo.Length > 0)
        {
            RaycastHit2D hit = hitInfo[0];
            Entity target = hit.collider.gameObject.GetComponent<Entity>();

            // Get the opponent player via PlayerInfo
            PlayerInfo opponentInfo = Player.localPlayer.hasEnemy ? Player.localPlayer.enemyInfo : default;
            bool hasTauntCreatures = opponentInfo.player != null && opponentInfo.tauntCount > 0;

            bool canTarget = target.casterType.CanTarget(card.acceptableTargets);

            if (target && !target.isTargeting && target.isTargetable && canTarget)
            {
                if (hasTauntCreatures)
                {
                    // If there are taunt creatures, block targeting the Player
                    if (target is Player)
                    {
                        spriteRenderer.sprite = defaultHead; // Cannot target Player when taunt exists
                        Debug.Log($"Cannot target {target.gameObject.name}: Taunt creatures present (tauntCount: {opponentInfo.tauntCount})");
                    }
                    else if (target is FieldCard)
                    {
                        // Allow targeting any FieldCard (taunt or non-taunt)
                        spriteRenderer.sprite = targetHead;
                        if (Input.GetMouseButtonDown(0))
                        {
                            if (!IsAbility) ((CreatureCard)card.data).Attack(caster, target);
                        }
                    }
                    else
                    {
                        spriteRenderer.sprite = defaultHead;
                    }
                }
                else
                {
                    // No taunt creatures, allow targeting based on acceptableTargets
                    spriteRenderer.sprite = targetHead;
                    if (Input.GetMouseButtonDown(0))
                    {
                        if (!IsAbility) ((CreatureCard)card.data).Attack(caster, target);
                    }
                }
            }
            else
            {
                spriteRenderer.sprite = defaultHead;
            }
        }
        else if (spriteRenderer.sprite != defaultHead)
        {
            spriteRenderer.sprite = defaultHead;
        }
    }
}