using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class Decklist
{
    public const string PlayerPrefsKey = "Deck";

    public const string PoolFolder = "Cards";

    public const int Size = 40;

    public const int MaxCopies = 2;

    private static List<CardDefinition> pool;

    public static List<CardDefinition> Pool()
    {
        if (pool != null) return pool;

        pool = new List<CardDefinition>();

        foreach (CardDefinition card in Resources.LoadAll<CardDefinition>(PoolFolder))
        {
            if (card == null || string.IsNullOrEmpty(card.CardID)) continue;

            pool.Add(card);
        }

        pool.Sort(delegate (CardDefinition left, CardDefinition right)
        {
            return string.CompareOrdinal(left.CardID, right.CardID);
        });

        return pool;
    }

    public static bool InPool(string cardId)
    {
        foreach (CardDefinition card in Pool())
            if (card.CardID == cardId) return true;

        return false;
    }

    public static string Encode(IList<DeckEntry> entries)
    {
        if (entries == null) return "";

        StringBuilder sb = new StringBuilder();

        foreach (DeckEntry entry in entries)
        {
            if (entry.card == null || entry.amount <= 0) continue;

            if (sb.Length > 0) sb.Append(',');

            sb.Append(entry.card.CardID).Append(':').Append(entry.amount);
        }

        return sb.ToString();
    }

    public static bool Parse(string wire, out List<CardInfo> composition, out string trouble)
    {
        composition = new List<CardInfo>();
        trouble = "";

        if (string.IsNullOrEmpty(wire))
        {
            trouble = "no decklist";
            return false;
        }

        Dictionary<string, int> counted = new Dictionary<string, int>();

        foreach (string part in wire.Split(','))
        {
            string piece = part.Trim();
            if (piece.Length == 0) continue;

            string[] bits = piece.Split(':');
            if (bits.Length != 2)
            {
                trouble = "\"" + piece + "\" is not a card and a count";
                return false;
            }

            string cardId = bits[0].Trim();
            int amount;

            if (!int.TryParse(bits[1].Trim(), out amount) || amount <= 0)
            {
                trouble = "\"" + piece + "\" has no usable count";
                return false;
            }

            if (!InPool(cardId))
            {
                trouble = "card " + cardId + " is not in the pool";
                return false;
            }

            if (counted.ContainsKey(cardId))
            {
                trouble = "card " + cardId + " is listed twice";
                return false;
            }

            if (amount > MaxCopies)
            {
                trouble = "card " + cardId + " has " + amount + " copies, the limit is " + MaxCopies;
                return false;
            }

            counted[cardId] = amount;
        }

        int total = 0;
        foreach (KeyValuePair<string, int> entry in counted) total += entry.Value;

        if (total != Size)
        {
            trouble = "a deck is " + Size + " cards, this one is " + total;
            return false;
        }

        foreach (CardDefinition card in Pool())
        {
            int amount;
            if (!counted.TryGetValue(card.CardID, out amount)) continue;

            for (int i = 0; i < amount; i++) composition.Add(new CardInfo(card));
        }

        return true;
    }

    public static string Canonical(string wire)
    {
        List<CardInfo> composition;
        string trouble;

        if (!Parse(wire, out composition, out trouble)) return "";

        Dictionary<string, int> counted = new Dictionary<string, int>();

        foreach (CardInfo info in composition)
        {
            if (info.data == null) continue;

            string id = info.data.CardID;
            counted[id] = counted.ContainsKey(id) ? counted[id] + 1 : 1;
        }

        StringBuilder sb = new StringBuilder();

        foreach (CardDefinition card in Pool())
        {
            int amount;
            if (!counted.TryGetValue(card.CardID, out amount)) continue;

            if (sb.Length > 0) sb.Append(',');

            sb.Append(card.CardID).Append(':').Append(amount);
        }

        return sb.ToString();
    }

    public static bool Legal(string wire)
    {
        List<CardInfo> composition;
        string trouble;

        return Parse(wire, out composition, out trouble);
    }

    public static string Load()
    {
        return PlayerPrefs.GetString(PlayerPrefsKey, "");
    }

    public static void Save(string wire)
    {
        PlayerPrefs.SetString(PlayerPrefsKey, wire ?? "");
        PlayerPrefs.Save();
    }

    public static void Forget()
    {
        pool = null;
    }
}
