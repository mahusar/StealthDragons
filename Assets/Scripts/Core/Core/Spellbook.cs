using System.Collections.Generic;
using Mirror;
using UnityEngine;

public static class Spellbook
{
    public static bool Resolve(SpellCard spell, Player caster, BoardCard chosen, out string trouble)
    {
        trouble = "";

        if (!NetworkServer.active)
        {
            trouble = "only the server resolves a spell";
            return false;
        }

        if (spell == null || caster == null)
        {
            trouble = "no spell or no caster";
            return false;
        }

        List<BoardCard> hit;
        if (!Targets(spell, caster, chosen, out hit, out trouble)) return false;

        foreach (BoardCard card in hit) Apply(spell, card);

        if (spell.cardDraw > 0 && caster.deck != null) caster.deck.ServerDrawCards(spell.cardDraw);

        Debug.Log("Spellbook: " + caster.username + " cast " + spell.name + " on " + Named(hit) + ".");
        return true;
    }

    public static bool Legal(SpellCard spell, Player caster, BoardCard chosen, out string trouble)
    {
        trouble = "";

        if (spell == null || caster == null)
        {
            trouble = "no spell or no caster";
            return false;
        }

        if (chosen == null || chosen.health <= 0 || !chosen.isTargetable)
        {
            trouble = "that target is gone";
            return false;
        }

        bool mine = chosen.owner == caster;

        if (spell.Harmful && mine)
        {
            trouble = "a harmful spell cannot be aimed at your own creature";
            return false;
        }

        if (!spell.Harmful && !mine)
        {
            trouble = "a helpful spell only reaches your own creatures";
            return false;
        }

        if (!spell.Harmful) return true;

        Player defender = chosen.owner;

        if (defender != null && defender.tauntCount > 0 && !chosen.taunt)
        {
            trouble = "a taunt creature has to be targeted first";
            return false;
        }

        return true;
    }

    public static List<BoardCard> Preview(SpellCard spell, Player caster)
    {
        List<BoardCard> shown = new List<BoardCard>();

        if (spell == null || caster == null) return shown;

        if (spell.targeted)
        {
            string trouble;

            foreach (BoardCard card in Pool(caster, !spell.Harmful))
                if (Legal(spell, caster, card, out trouble)) shown.Add(card);

            return shown;
        }

        if (spell.affects == Target.OWNER) return shown;

        List<BoardCard> pool = Pool(caster, spell.affects == Target.FRIENDLIES);

        if (spell.affects == Target.RANDOM) return spell.Harmful ? Guarded(pool) : pool;

        return pool;
    }

    private static bool Targets(SpellCard spell, Player caster, BoardCard chosen,
                               out List<BoardCard> hit, out string trouble)
    {
        hit = new List<BoardCard>();
        trouble = "";

        if (spell.targeted)
        {
            if (chosen == null)
            {
                trouble = "this spell needs a target";
                return false;
            }

            if (!Legal(spell, caster, chosen, out trouble)) return false;

            hit.Add(chosen);
            return true;
        }

        if (spell.affects == Target.OWNER) return true;

        List<BoardCard> pool = Pool(caster, spell.affects == Target.FRIENDLIES);

        if (spell.affects == Target.RANDOM)
        {
            List<BoardCard> reachable = spell.Harmful ? Guarded(pool) : pool;
            if (reachable.Count == 0) return true;

            hit.Add(reachable[MatchRandom.Below(reachable.Count)]);
            return true;
        }

        hit.AddRange(pool);
        return true;
    }

    private static List<BoardCard> Pool(Player caster, bool friendly)
    {
        List<BoardCard> found = new List<BoardCard>();

        foreach (BoardCard card in Object.FindObjectsByType<BoardCard>(FindObjectsSortMode.None))
        {
            if (card == null || card.health <= 0 || card.owner == null) continue;
            if ((card.owner == caster) != friendly) continue;

            found.Add(card);
        }

        found.Sort(ByNetId);

        return found;
    }

    private static List<BoardCard> Guarded(List<BoardCard> pool)
    {
        List<BoardCard> guards = new List<BoardCard>();

        foreach (BoardCard card in pool)
            if (card.taunt) guards.Add(card);

        return guards.Count > 0 ? guards : pool;
    }

    private static void Apply(SpellCard spell, BoardCard card)
    {
        if (card == null) return;

        if (spell.strengthChange != 0 && card.combat != null)
            card.combat.ServerChangeStrength(spell.strengthChange);

        if (spell.healthChange < 0) Combat.ServerDealDamage(card, -spell.healthChange);
        else if (spell.healthChange > 0 && card.combat != null)
            card.combat.ServerChangeHealth(spell.healthChange);
    }

    private static int ByNetId(BoardCard left, BoardCard right)
    {
        return left.netId.CompareTo(right.netId);
    }

    private static string Named(List<BoardCard> cards)
    {
        if (cards == null || cards.Count == 0) return "nothing";

        List<string> names = new List<string>();

        foreach (BoardCard card in cards)
            names.Add(card.card.Known ? card.card.displayName : "an unknown card");

        return string.Join(", ", names.ToArray());
    }
}
