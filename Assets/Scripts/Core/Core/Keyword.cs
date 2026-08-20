using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public static class Keyword
{
    public static readonly Color Taunt = new Color(0.35f, 0.62f, 1f, 1f);
    public static readonly Color Shield = new Color(1f, 0.82f, 0.25f, 1f);
    public static readonly Color Lifesteal = new Color(0.35f, 0.95f, 0.45f, 1f);
    public static readonly Color Charge = new Color(1f, 0.55f, 0.15f, 1f);
    public static readonly Color Deathrattle = new Color(0.72f, 0.45f, 1f, 1f);
    public static readonly Color None = Color.white;

    public const int SmallSize = 14;

    public const float StackedSpacing = 0.78f;

    public static bool Any(CreatureCard creature, bool shielded)
    {
        if (creature == null) return false;

        return shielded || creature.hasTaunt || creature.hasLifesteal || creature.hasCharge;
    }

    public static Color Tint(CreatureCard creature, bool shielded)
    {
        if (creature == null) return None;

        if (shielded) return Shield;
        if (creature.hasTaunt) return Taunt;
        if (creature.hasLifesteal) return Lifesteal;
        if (creature.hasCharge) return Charge;

        return None;
    }

    public static string Label(CreatureCard creature)
    {
        if (creature == null) return "";

        List<string> words = new List<string>();

        if (creature.hasTaunt) words.Add("TAUNT");
        if (creature.hasCharge) words.Add("CHARGE");
        if (creature.hasLifesteal) words.Add("LIFESTEAL");
        if (creature.hasShield) words.Add("SHIELD");
        if (creature.hasDeathrattle) words.Add("DEATHRATTLE");

        return string.Join(" / ", words.ToArray());
    }

    public static string RichLabel(CreatureCard creature)
    {
        if (creature == null) return "";

        List<string> words = new List<string>();

        if (creature.hasTaunt) words.Add(Wrap("TAUNT", Taunt));
        if (creature.hasCharge) words.Add(Wrap("CHARGE", Charge));
        if (creature.hasLifesteal) words.Add(Wrap("LIFESTEAL", Lifesteal));
        if (creature.hasShield) words.Add(Wrap("SHIELD", Shield));

        string line = string.Join(" / ", words.ToArray());

        if (!creature.hasDeathrattle) return line;

        string rattle = Wrap("DEATHRATTLE", Deathrattle);

        if (line.Length == 0) return rattle;

        return line + "\n<size=" + SmallSize + ">" + rattle + "</size>";
    }

    public static bool Stacked(CreatureCard creature)
    {
        return creature != null && creature.hasDeathrattle && HasStandard(creature);
    }

    private static bool HasStandard(CreatureCard creature)
    {
        return creature.hasTaunt || creature.hasCharge || creature.hasLifesteal || creature.hasShield;
    }

    public static void Fit(Text label, CreatureCard creature)
    {
        if (label == null) return;

        bool stacked = Stacked(creature);

        label.supportRichText = true;
        label.resizeTextForBestFit = !stacked;
        label.verticalOverflow = stacked ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;
        label.lineSpacing = stacked ? StackedSpacing : 1f;

        if (stacked) label.fontSize = label.resizeTextMaxSize;
    }

    private static string Wrap(string word, Color tint)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(tint) + ">" + word + "</color>";
    }

    public static string TypeOf(CreatureCard creature)
    {
        if (creature == null || creature.creatureType == null || creature.creatureType.Count == 0) return "";

        string kind = creature.creatureType[0].ToString();

        return kind.Substring(0, 1) + kind.Substring(1).ToLowerInvariant();
    }
}
