using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Card/Spell Card", order = 111)]
public partial class SpellCard : CardDefinition
{
    [Header("Propeties")]
    public bool targeted = false;
    public Target affects = Target.ENEMIES;
    public int healthChange = 0;
    public int strengthChange = 0;
    public int cardDraw = 0;
    public bool destroys = false;
    public int bolts = 1;
    public bool onlyOneKind = false;
    public CreatureKind kind = CreatureKind.DRAGON;
    public bool untilEndOfTurn = false;

    public bool Harmful
    {
        get { return destroys || healthChange < 0 || strengthChange < 0; }
    }

    public bool Draws
    {
        get { return cardDraw != 0; }
    }

    public int Bolts
    {
        get { return Mathf.Max(1, bolts); }
    }

    public bool Scatters
    {
        get { return !targeted && affects == Target.RANDOM; }
    }

    public bool Reaches(CreatureCard creature)
    {
        if (!onlyOneKind) return true;
        if (creature == null) return false;

        List<CreatureKind> kinds = creature.creatureType;
        if (kinds == null) return false;

        for (int i = 0; i < kinds.Count; i++)
            if (kinds[i] == kind || kinds[i] == CreatureKind.ALL) return true;

        return false;
    }
}
