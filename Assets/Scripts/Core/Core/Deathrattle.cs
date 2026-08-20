using System.Collections.Generic;
using Mirror;
using UnityEngine;

public static class Deathrattle
{
    public static bool On(CreatureCard creature)
    {
        return creature != null && creature.hasDeathrattle && creature.deathrattleDamage > 0;
    }

    public static int DamageOf(BoardCard card)
    {
        CreatureCard creature = Creature(card);

        return On(creature) ? creature.deathrattleDamage : 0;
    }

    public static BoardCard Resolve(BoardCard dying)
    {
        if (!NetworkServer.active) return null;
        if (dying == null || dying.deathrattleSpent) return null;

        CreatureCard creature = Creature(dying);
        if (!On(creature)) return null;

        dying.deathrattleSpent = true;

        List<BoardCard> enemies = Enemies(dying);

        if (enemies.Count == 0)
        {
            Debug.Log("Deathrattle: " + Name(dying) + " went off with no enemy creature left to hit.");
            return null;
        }

        int pick = MatchRandom.Below(enemies.Count);
        BoardCard target = enemies[pick];

        Debug.Log("Deathrattle: " + Name(dying) + " deals " + creature.deathrattleDamage + " to " +
                  Name(target) + " - draw " + (MatchRandom.Drawn - 1) + " of the match picked " +
                  pick + " of " + enemies.Count + " target(s), netId " + target.netId + ".");

        Combat.ServerDealDamage(target, creature.deathrattleDamage);

        return target;
    }

    private static List<BoardCard> Enemies(BoardCard dying)
    {
        List<BoardCard> enemies = new List<BoardCard>();

        foreach (BoardCard card in Object.FindObjectsByType<BoardCard>(FindObjectsSortMode.None))
        {
            if (card == null || card == dying) continue;
            if (card.health <= 0) continue;
            if (card.owner == null || card.owner == dying.owner) continue;

            enemies.Add(card);
        }

        enemies.Sort(ByNetId);

        return enemies;
    }

    private static int ByNetId(BoardCard left, BoardCard right)
    {
        return left.netId.CompareTo(right.netId);
    }

    private static CreatureCard Creature(BoardCard card)
    {
        if (card == null || !card.card.Known) return null;

        return card.card.data as CreatureCard;
    }

    private static string Name(BoardCard card)
    {
        if (card == null) return "a card that is already gone";

        return card.card.Known ? card.card.displayName : "an unknown card";
    }
}
