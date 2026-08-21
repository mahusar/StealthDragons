using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

public static class CardFingerprint
{
    public const int Length = 16;

    public static string Of(List<CardInfo> composition)
    {
        if (composition == null || composition.Count == 0) return "";

        List<string> parts = new List<string>();

        foreach (CardInfo info in composition)
            parts.Add(info.Known ? Describe(info.data) : "?");

        parts.Sort(StringComparer.Ordinal);

        using (SHA256 sha = SHA256.Create())
            return CardShuffle.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join(",", parts.ToArray()))))
                   .Substring(0, Length);
    }

    private static string Describe(CardDefinition card)
    {
        CreatureCard creature = card as CreatureCard;

        if (creature != null)
            return card.CardID + ":c:" + creature.cost + ":" + creature.strength + ":" + creature.health +
                   ":" + Flag(creature.hasTaunt) + Flag(creature.hasCharge) +
                   Flag(creature.hasLifesteal) + Flag(creature.hasShield) +
                   ":" + (creature.hasDeathrattle ? creature.deathrattleDamage : 0);

        SpellCard spell = card as SpellCard;

        if (spell != null)
            return card.CardID + ":s:" + spell.cost + ":" + Flag(spell.targeted) + ":" + (int)spell.affects +
                   ":" + spell.healthChange + ":" + spell.strengthChange + ":" + spell.cardDraw;

        return card.CardID + ":?:" + card.cost;
    }

    private static string Flag(bool on)
    {
        return on ? "1" : "0";
    }
}
