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

    [HideInInspector] public int handIndex = -1;

    private Entity aimed;

    public void FindTargets(Entity caster, Vector2 mousePos, bool IsAbility)
    {
        Entity target = Under(mousePos);
        SpellCard spell = Spell();

        bool reachable = spell != null
            ? Castable(spell, target)
            : Reachable(target) && (!Walled() || Guards(target));

        Aim(reachable ? target : null);

        if (!reachable)
        {
            ShowHead(defaultHead);

            if (IsAbility && Input.GetMouseButtonDown(0)) Release();

            return;
        }

        ShowHead(targetHead);

        if (!Input.GetMouseButtonDown(0)) return;

        if (spell != null)
        {
            if (IsAbility) PowerAt(target);
            else CastAt(target);

            AimHighlight.Clear();
            return;
        }

        if (IsAbility) return;

        CreatureCard creature = card.data as CreatureCard;
        if (creature == null) return;

        creature.Attack(caster, target);
    }

    private SpellCard Spell()
    {
        return card.Known ? card.data as SpellCard : null;
    }

    private static bool Castable(SpellCard spell, Entity target)
    {
        BoardCard creature = target as BoardCard;
        if (creature == null || creature.isTargeting) return false;

        string trouble;
        return Spellbook.Legal(spell, Player.localPlayer, creature, out trouble);
    }

    private void Release()
    {
        AimHighlight.Clear();

        Player me = Player.localPlayer;
        if (me != null) me.DestroyTargetingArrow();
    }

    private void PowerAt(Entity target)
    {
        BoardCard creature = target as BoardCard;
        Player me = Player.localPlayer;

        if (creature == null || me == null || me.deck == null) return;

        me.deck.CmdUseHeroPower(creature.netId);
        me.DestroyTargetingArrow();
    }

    private void CastAt(Entity target)
    {
        BoardCard creature = target as BoardCard;
        Player me = Player.localPlayer;

        if (creature == null || me == null || me.deck == null || handIndex < 0) return;

        me.deck.CmdCastSpell(handIndex, creature.netId);
        me.DestroyTargetingArrow();
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
