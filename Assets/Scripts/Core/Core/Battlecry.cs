using Mirror;
using UnityEngine;

public static class Battlecry
{
    public static bool On(CreatureCard creature)
    {
        return Of(creature) != null;
    }

    public static SpellCard Of(CreatureCard creature)
    {
        if (creature == null || creature.battlecry == null) return null;

        return creature.battlecry.targeted ? null : creature.battlecry;
    }

    public static SpellCard Of(BoardCard card)
    {
        return Of(Creature(card));
    }

    public static string Line(CreatureCard creature)
    {
        SpellCard cry = Of(creature);

        return cry == null ? "" : SpellText.Line(cry);
    }

    public static bool Resolve(BoardCard played)
    {
        if (!NetworkServer.active) return false;
        if (played == null || played.battlecrySpent) return false;

        SpellCard cry = Of(played);
        if (cry == null) return false;

        Player owner = played.owner;
        if (owner == null) return false;

        played.battlecrySpent = true;

        string trouble;
        if (!Spellbook.Resolve(cry, owner, null, out trouble))
        {
            Debug.LogWarning("Battlecry: " + Name(played) + " went off but nothing happened - " + trouble);
            return false;
        }

        Debug.Log("Battlecry: " + Name(played) + " went off with " + SpellText.Line(cry) + ".");

        return true;
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
