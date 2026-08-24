using UnityEngine;
using System.Collections.Generic;
using Mirror;

public enum CreatureKind : byte { BEAST, DRAGON, ALL }

[CreateAssetMenu(menuName = "Card/Creature Card", order = 111)]
public partial class CreatureCard : CardDefinition
{
    [Header("Stats")]
    public int strength;
    public int health;

    [Header("Targets")]
    public List<Target> acceptableTargets = new List<Target>();

    [Header("Type")]
    public List<CreatureKind> creatureType;

    [Header("Specialities")]
    public bool hasCharge = false;
    public bool hasTaunt = false;
    public bool hasLifesteal = false;
    public bool hasShield = false;

    [Header("Deathrattle")]
    public bool hasDeathrattle = false;
    public int deathrattleDamage = 0;

    [Header("Battlecry")]
    public SpellCard battlecry;

    [Header("Board Prefab")]
    public BoardCard cardPrefab;

    public static bool Guards(Entity target)
    {
        BoardCard card = target as BoardCard;
        if (card == null || !card.card.Known) return false;

        CreatureCard creature = card.card.data as CreatureCard;

        return creature != null && creature.hasTaunt;
    }

    public void Attack(Entity attacker, Entity target)
    {
        if (attacker == null || target == null)
        {
            Debug.LogError("Attack: Attacker or Target is null!");
            return;
        }

        PlayerInfo opponentInfo = attacker.owner.hasEnemy ? attacker.owner.enemyInfo : default;
        bool hasTauntCreatures = opponentInfo.player != null && opponentInfo.tauntCount > 0;

        if (hasTauntCreatures && !Guards(target))
        {
            Debug.LogWarning($"Attack: {target.gameObject.name} cannot be attacked while " +
                             $"{opponentInfo.tauntCount} taunt creature(s) are on the board.");
            return;
        }

        Player.localPlayer.CmdRequestAttack(attacker.netId, target.netId);

        attacker.DestroyTargetingArrow();
    }
}
