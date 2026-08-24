using Mirror;
using UnityEngine;

public static class HeroPower
{
    public static SpellCard Of(Player player)
    {
        return player == null ? null : player.heroPower;
    }

    public static int CostOf(Player player)
    {
        return player == null ? 0 : Mathf.Max(0, player.heroPowerCost);
    }

    public static bool Has(Player player)
    {
        return Of(player) != null;
    }

    public static bool Targets(Player player)
    {
        SpellCard power = Of(player);

        return power != null && power.targeted;
    }

    public static string Line(Player player)
    {
        SpellCard power = Of(player);

        return power == null ? "" : SpellText.Line(power);
    }

    public static bool Spent(Player player)
    {
        return player != null && player.heroPowerUsed;
    }

    public static bool Affordable(Player player)
    {
        return player != null && player.mana >= CostOf(player);
    }

    public static bool Ready(Player player)
    {
        return Has(player) && !Spent(player) && Affordable(player);
    }

    public static bool Resolve(Player caster, BoardCard chosen, out string trouble)
    {
        trouble = "";

        if (!NetworkServer.active)
        {
            trouble = "only the server resolves a hero power";
            return false;
        }

        SpellCard power = Of(caster);

        if (power == null)
        {
            trouble = "this hero has no power";
            return false;
        }

        if (Spent(caster))
        {
            trouble = "the hero power has already been used this turn";
            return false;
        }

        if (!Affordable(caster))
        {
            trouble = "the hero power costs " + CostOf(caster) + " and there is " + caster.mana + " left";
            return false;
        }

        if (!Spellbook.Resolve(power, caster, chosen, out trouble)) return false;

        if (caster.combat != null) caster.combat.ServerChangeMana(-CostOf(caster));
        caster.heroPowerUsed = true;

        Debug.Log("HeroPower: " + caster.username + " used " + power.name + " (" + SpellText.Line(power) + ").");

        return true;
    }
}
