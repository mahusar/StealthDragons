using Mirror;
using UnityEngine;

public class AimTip : MonoBehaviour
{
    [Header("Arrow Heads")]
    public Sprite defaultHead;
    public Sprite targetHead;

    [Header("Properties")]
    public SpriteRenderer spriteRenderer;
    public LayerMask targetLayer;

    [HideInInspector] public CardInfo card;

    private Entity aimed;

    public void FindTargets(Entity caster, Vector2 mousePos, bool IsAbility)
    {
        Entity target = Under(mousePos);
        bool reachable = Reachable(target) && (!Walled() || Guards(target));

        Aim(reachable ? target : null);

        if (!reachable)
        {
            ShowHead(defaultHead);
            return;
        }

        ShowHead(targetHead);

        if (!Input.GetMouseButtonDown(0) || IsAbility) return;

        CreatureCard creature = card.data as CreatureCard;
        if (creature == null) return;

        creature.Attack(caster, target);
    }

    private void Aim(Entity target)
    {
        if (aimed == target) return;

        Mark(aimed, false);
        aimed = target;
        Mark(aimed, true);
    }

    private static void Mark(Entity target, bool aimed)
    {
        if (target == null) return;

        AimHighlight.Paint(target.gameObject, aimed);

        if (Player.gameManager == null) return;
        if (!NetworkClient.active || !NetworkClient.ready) return;

        Player.gameManager.CmdAimAt(target.gameObject, aimed);
    }

    private void OnDestroy()
    {
        Aim(null);
    }

    private Entity Under(Vector2 mousePos)
    {
        RaycastHit2D[] hits = Physics2D.CircleCastAll(mousePos, 0.1f, Vector2.zero, 1f, targetLayer);

        if (hits.Length == 0) return null;
        if (hits[0].collider == null) return null;

        return hits[0].collider.GetComponent<Entity>();
    }

    private bool Reachable(Entity target)
    {
        if (target == null) return false;
        if (target.isTargeting || !target.isTargetable) return false;

        Player hero = target as Player;
        if (hero != null && hero == Player.localPlayer) return false;

        return target.casterType.CanTarget(card.acceptableTargets);
    }

    private static bool Guards(Entity target)
    {
        BoardCard card = target as BoardCard;
        if (card == null || !card.card.Known) return false;

        CreatureCard creature = card.card.data as CreatureCard;

        return creature != null && creature.hasTaunt;
    }

    private bool Walled()
    {
        Player me = Player.localPlayer;

        if (me == null || !me.hasEnemy) return false;

        PlayerInfo enemy = me.enemyInfo;

        return enemy.player != null && enemy.tauntCount > 0;
    }

    private void ShowHead(Sprite head)
    {
        if (spriteRenderer == null || spriteRenderer.sprite == head) return;

        spriteRenderer.sprite = head;
    }
}
