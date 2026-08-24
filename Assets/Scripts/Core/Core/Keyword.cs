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
    public static readonly Color Battlecry = new Color(1f, 0.78f, 0.55f, 1f);
    public static readonly Color None = Color.white;

    public const int SmallSize = 14;

    public const int SoloSize = 20;

    private const int SoloFits = 18;

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
        if (global::Battlecry.On(creature)) words.Add("BATTLECRY " + global::Battlecry.Line(creature));

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

        List<string> extras = new List<string>();

        if (creature.hasDeathrattle) extras.Add(Wrap("DEATHRATTLE", Deathrattle));

        if (global::Battlecry.On(creature))
            extras.Add(Wrap("BATTLECRY " + global::Battlecry.Line(creature), Battlecry));

        if (extras.Count == 0) return line;

        string extra = string.Join(" / ", extras.ToArray());

        if (line.Length == 0)
            return SoloLong(creature) ? "<size=" + SoloSize + ">" + extra + "</size>" : extra;

        return line + "\n<size=" + SmallSize + ">" + extra + "</size>";
    }

    public static bool Stacked(CreatureCard creature)
    {
        if (creature == null) return false;
        if (!HasExtra(creature)) return false;

        return HasStandard(creature) || SoloLong(creature);
    }

    private static bool HasExtra(CreatureCard creature)
    {
        return creature.hasDeathrattle || global::Battlecry.On(creature);
    }

    private static bool SoloLong(CreatureCard creature)
    {
        if (HasStandard(creature)) return false;

        return ExtraWords(creature).Length > SoloFits;
    }

    private static string ExtraWords(CreatureCard creature)
    {
        List<string> words = new List<string>();

        if (creature.hasDeathrattle) words.Add("DEATHRATTLE");

        if (global::Battlecry.On(creature))
            words.Add("BATTLECRY " + global::Battlecry.Line(creature));

        return string.Join(" / ", words.ToArray());
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
