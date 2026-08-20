using System;
using System.Globalization;
using Mirror;
using UnityEngine;

public enum BotActionResult
{
    Played,
    Attacked,
    Ended,
    Refused,
    Malformed,
}

public static class BotAction
{
    public const string Play = "play";
    public const string Attack = "attack";
    public const string End = "end";

    public static BotActionResult Apply(string line, Player self, GameManager gameManager, out string detail)
    {
        detail = "";

        if (string.IsNullOrEmpty(line))
        {
            detail = "empty action";
            return BotActionResult.Malformed;
        }

        string[] parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            detail = "empty action";
            return BotActionResult.Malformed;
        }

        string verb = parts[0].ToLowerInvariant();

        if (verb == End) return BotActionResult.Ended;

        if (verb == Play)
        {
            if (parts.Length != 2)
            {
                detail = "play needs one hand index";
                return BotActionResult.Malformed;
            }

            int index;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                detail = "play index is not a number";
                return BotActionResult.Malformed;
            }

            if (!Seated(self, gameManager, out detail)) return BotActionResult.Refused;

            return ApplyPlay(index, self, out detail);
        }

        if (verb == Attack)
        {
            if (parts.Length != 3)
            {
                detail = "attack needs an attacker and a target";
                return BotActionResult.Malformed;
            }

            uint attacker;
            uint target;

            if (!uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out attacker) ||
                !uint.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out target))
            {
                detail = "attack ids are not numbers";
                return BotActionResult.Malformed;
            }

            if (!Seated(self, gameManager, out detail)) return BotActionResult.Refused;

            return ApplyAttack(attacker, target, self, out detail);
        }

        detail = "unknown action " + Trim(verb);
        return BotActionResult.Malformed;
    }

    private static bool Seated(Player self, GameManager gameManager, out string detail)
    {
        detail = "";

        if (self != null && gameManager != null) return true;

        detail = "no player or game manager";
        return false;
    }

    private static BotActionResult ApplyPlay(int index, Player self, out string detail)
    {
        detail = "";

        if (self.deck == null)
        {
            detail = "no deck";
            return BotActionResult.Refused;
        }

        int before = self.deck.hand.Count;

        self.deck.ServerPlayCard(index);

        if (self.deck.hand.Count < before) return BotActionResult.Played;

        detail = "the server refused play " + index;
        return BotActionResult.Refused;
    }

    private static BotActionResult ApplyAttack(uint attackerNetId, uint targetNetId, Player self, out string detail)
    {
        detail = "";

        NetworkIdentity identity;
        BoardCard attacker = NetworkServer.spawned.TryGetValue(attackerNetId, out identity) && identity != null
            ? identity.GetComponent<BoardCard>()
            : null;

        bool attackedBefore = attacker != null && attacker.hasAttackedThisTurn;

        self.ServerRequestAttack(attackerNetId, targetNetId);

        if (attacker == null)
        {
            detail = "attacker " + attackerNetId + " is not on the board";
            return BotActionResult.Refused;
        }

        if (attacker.hasAttackedThisTurn && !attackedBefore) return BotActionResult.Attacked;

        detail = "the server refused attack " + attackerNetId + " " + targetNetId;
        return BotActionResult.Refused;
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= 24 ? text : text.Substring(0, 24);
    }
}
