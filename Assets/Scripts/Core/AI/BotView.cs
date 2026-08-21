using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Mirror;
using UnityEngine;

public static class BotView
{
    public const int Protocol = 1;

    public static string Build(Player self, GameManager gameManager)
    {
        if (self == null || gameManager == null) return null;

        Player opponent = FindOpponent(self);

        List<BoardCard> ours = new List<BoardCard>();
        List<BoardCard> theirs = new List<BoardCard>();
        SplitField(self, ours, theirs);

        StringBuilder json = new StringBuilder(1024);

        json.Append('{');
        Number(json, "protocol", Protocol, true);
        Number(json, "turn", gameManager.turnCount, false);
        Bool(json, "yourTurn", gameManager.currentTurnNetId == self.netId, false);
        Number(json, "secondsLeft", SecondsLeft(gameManager), false);

        Key(json, "you", false);
        Seat(json, self, ours.Count);

        Key(json, "opponent", false);
        if (opponent == null) json.Append("null");
        else Seat(json, opponent, theirs.Count);

        Key(json, "hand", false);
        Hand(json, self);

        Key(json, "yourField", false);
        Field(json, ours);

        Key(json, "enemyField", false);
        Field(json, theirs);

        json.Append('}');

        return json.ToString();
    }

    private static void Seat(StringBuilder json, Player player, int fieldCount)
    {
        json.Append('{');
        Number(json, "netId", (int)player.netId, true);
        Text(json, "name", player.username, false);
        Number(json, "health", player.health, false);
        Number(json, "mana", player.mana, false);
        Number(json, "handCount", player.deck == null ? 0 : player.deck.hand.Count, false);
        Number(json, "deckCount", player.deck == null ? 0 : player.deck.deckList.Count, false);
        Number(json, "fieldCount", fieldCount, false);
        Number(json, "taunt", player.tauntCount, false);
        Bool(json, "targetable", player.isTargetable, false);
        json.Append('}');
    }

    private static void Hand(StringBuilder json, Player player)
    {
        json.Append('[');

        if (player.deck != null)
        {
            for (int i = 0; i < player.deck.hand.Count; i++)
            {
                if (i > 0) json.Append(',');

                CardInfo info = player.deck.hand[i];
                CardDefinition data = Data(info);

                json.Append('{');
                Number(json, "index", i, true);

                if (data == null)
                {
                    Text(json, "kind", "unknown", false);
                    json.Append('}');
                    continue;
                }

                Text(json, "cardId", info.cardID, false);
                Text(json, "name", data.name, false);
                Number(json, "cost", data.cost, false);

                CreatureCard creature = data as CreatureCard;
                if (creature != null)
                {
                    Text(json, "kind", "creature", false);
                    Number(json, "strength", creature.strength, false);
                    Number(json, "health", creature.health, false);
                    Bool(json, "charge", creature.hasCharge, false);
                    Bool(json, "taunt", creature.hasTaunt, false);
                    Bool(json, "lifesteal", creature.hasLifesteal, false);
                    Bool(json, "shield", creature.hasShield, false);
                    Bool(json, "deathrattle", Deathrattle.On(creature), false);
                    Number(json, "deathrattleDamage", Deathrattle.On(creature) ? creature.deathrattleDamage : 0, false);
                }
                else if (data is SpellCard sorcery)
                {
                    Text(json, "kind", "spell", false);
                    Bool(json, "targeted", sorcery.targeted, false);
                    Text(json, "affects", sorcery.affects.ToString().ToLowerInvariant(), false);
                    Number(json, "healthChange", sorcery.healthChange, false);
                    Number(json, "strengthChange", sorcery.strengthChange, false);
                    Number(json, "cardDraw", sorcery.cardDraw, false);
                }
                else
                {
                    Text(json, "kind", "other", false);
                }

                json.Append('}');
            }
        }

        json.Append(']');
    }

    private static void Field(StringBuilder json, List<BoardCard> cards)
    {
        json.Append('[');

        for (int i = 0; i < cards.Count; i++)
        {
            if (i > 0) json.Append(',');

            BoardCard card = cards[i];
            CardDefinition data = Data(card.card);

            json.Append('{');
            Number(json, "netId", (int)card.netId, true);
            Text(json, "cardId", card.card.cardID, false);
            Text(json, "name", data == null ? "unknown" : data.name, false);
            Number(json, "strength", card.strength, false);
            Number(json, "health", card.health, false);
            Number(json, "waitTurn", card.waitTurn, false);
            Bool(json, "attacked", card.hasAttackedThisTurn, false);
            Bool(json, "taunt", card.taunt, false);
            Bool(json, "lifesteal", Lifesteal(card), false);
            Bool(json, "shield", card.shielded, false);
            Bool(json, "deathrattle", Deathrattle.DamageOf(card) > 0, false);
            Number(json, "deathrattleDamage", Deathrattle.DamageOf(card), false);
            Bool(json, "targetable", card.isTargetable, false);
            json.Append('}');
        }

        json.Append(']');
    }

    private static CardDefinition Data(CardInfo info)
    {
        if (string.IsNullOrEmpty(info.cardID)) return null;

        CardDefinition found;
        return CardDefinition.Cache.TryGetValue(info.cardID, out found) ? found : null;
    }

    private static void SplitField(Player self, List<BoardCard> ours, List<BoardCard> theirs)
    {
        foreach (BoardCard card in Object.FindObjectsByType<BoardCard>(FindObjectsSortMode.None))
        {
            if (card == null) continue;
            if (card.health <= 0) continue;

            if (card.owner == self) ours.Add(card);
            else theirs.Add(card);
        }
    }

    private static Player FindOpponent(Player self)
    {
        foreach (Player player in Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
            if (player != self && player.health > 0) return player;

        return null;
    }

    private static float SecondsLeft(GameManager gameManager)
    {
        if (gameManager.turnDeadline <= 0d) return 0f;

        double remaining = gameManager.turnDeadline - NetworkTime.time;
        return remaining <= 0d ? 0f : (float)remaining;
    }

    private static void Key(StringBuilder json, string name, bool first)
    {
        if (!first) json.Append(',');
        json.Append('"').Append(name).Append("\":");
    }

    private static void Number(StringBuilder json, string name, int value, bool first)
    {
        Key(json, name, first);
        json.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Number(StringBuilder json, string name, float value, bool first)
    {
        Key(json, name, first);
        json.Append(value.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private static bool Lifesteal(BoardCard card)
    {
        if (card == null || !card.card.Known) return false;

        CreatureCard creature = card.card.data as CreatureCard;
        return creature != null && creature.hasLifesteal;
    }

    private static void Bool(StringBuilder json, string name, bool value, bool first)
    {
        Key(json, name, first);
        json.Append(value ? "true" : "false");
    }

    private static void Text(StringBuilder json, string name, string value, bool first)
    {
        Key(json, name, first);
        json.Append('"').Append(Escape(value)).Append('"');
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        StringBuilder clean = new StringBuilder(value.Length + 8);

        foreach (char c in value)
        {
            if (c == '"' || c == '\\') clean.Append('\\').Append(c);
            else if (c == '\n') clean.Append("\\n");
            else if (c == '\r') clean.Append("\\r");
            else if (c == '\t') clean.Append("\\t");
            else if (c < ' ') clean.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else clean.Append(c);
        }

        return clean.ToString();
    }
}
