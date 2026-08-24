using UnityEngine;

public static class SpellText
{
    public static readonly Color Tint = new Color(1f, 0.55f, 0.9f, 1f);

    public static string Line(SpellCard spell)
    {
        if (spell == null) return "";

        if (spell.destroys) return spell.onlyOneKind ? "DESTROY A " + Tribe(spell) : "DESTROY";
        if (spell.healthChange < 0) return "DEAL " + (-spell.healthChange) + Scope(spell);
        if (spell.healthChange > 0) return "RESTORE " + spell.healthChange + Scope(spell);
        if (spell.strengthChange != 0)
            return (spell.strengthChange > 0 ? "+" : "") + spell.strengthChange + " STRENGTH" + Scope(spell);
        if (spell.cardDraw > 0) return "DRAW " + spell.cardDraw;

        return "SPELL";
    }

    private static string Tribe(SpellCard spell)
    {
        return spell.kind.ToString();
    }

    private static string Scope(SpellCard spell)
    {
        if (spell.targeted) return "";

        if (spell.affects == Target.ENEMIES) return " TO ALL ENEMIES";
        if (spell.affects == Target.FRIENDLIES) return spell.onlyOneKind ? " TO YOUR " + Tribe(spell) + "S" : " TO YOUR SIDE";
        if (spell.affects == Target.RANDOM)
            return spell.Bolts > 1 ? " AT RANDOM X" + spell.Bolts : " AT RANDOM";

        return "";
    }
}
