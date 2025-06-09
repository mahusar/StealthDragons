using UnityEngine;
using System.Collections.Generic;
using Mirror;

public enum CreatureType : byte { BEAST, DRAGON, ALL }

[CreateAssetMenu(menuName = "Card/Creature Card", order = 111)]
public partial class CreatureCard : ScriptableCard
{
    [Header("Stats")]
    public int strength;
    public int health;

    [Header("Targets")]
    public List<Target> acceptableTargets = new List<Target>();

    [Header("Type")]
    public List<CreatureType> creatureType;

    [Header("Specialities")]
    public bool hasCharge = false;
    public bool hasTaunt = false;

    [Header("Death Abilities")]
    public List<CardAbility> deathcrys = new List<CardAbility>();
    [HideInInspector] public bool hasDeathCry = false;

    [Header("Board Prefab")]
    public FieldCard cardPrefab;

    public void Attack(Entity attacker, Entity target)
    {
        if (attacker == null || target == null)
        {
            Debug.LogError("Attack: Attacker or Target is null!");
            return;
        }

        Combat attackerCombat = attacker.GetComponent<Combat>();
        Combat targetCombat = target.GetComponent<Combat>();

        if (attackerCombat == null || targetCombat == null)
        {
            Debug.LogError("Attack: Combat component missing on attacker or target!");
            return;
        }

        // Check taunt restriction
        PlayerInfo opponentInfo = attacker.owner.hasEnemy ? attacker.owner.enemyInfo : default;
        bool hasTauntCreatures = opponentInfo.player != null && opponentInfo.tauntCount > 0;
        if (hasTauntCreatures && target is Player)
        {
            Debug.LogError($"Attack: Cannot target {target.gameObject.name} because opponent has taunt creatures!");
            return;
        }

        // Proceed with attack
        attackerCombat.CmdAnimateAttack(attacker.gameObject, target.gameObject, attacker.strength, target.strength);
        targetCombat.CmdChangeHealth(-attacker.strength);
        attackerCombat.CmdChangeHealth(-target.strength);

        attacker.DestroyTargetingArrow();
        attackerCombat.CmdIncreaseWaitTurn();
    }
}