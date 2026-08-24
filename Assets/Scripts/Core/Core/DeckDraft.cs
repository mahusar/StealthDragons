using System.Collections.Generic;
using System.Text;

public class DeckDraft
{
    private readonly Dictionary<string, int> counts = new Dictionary<string, int>();

    public static DeckDraft From(string wire)
    {
        DeckDraft draft = new DeckDraft();

        if (string.IsNullOrEmpty(wire)) return draft;

        foreach (string part in wire.Split(','))
        {
            string piece = part.Trim();
            if (piece.Length == 0) continue;

            string[] bits = piece.Split(':');
            if (bits.Length != 2) continue;

            string cardId = bits[0].Trim();
            int amount;

            if (!int.TryParse(bits[1].Trim(), out amount)) continue;
            if (amount <= 0 || !Decklist.InPool(cardId)) continue;

            if (amount > Decklist.MaxCopies) amount = Decklist.MaxCopies;

            draft.counts[cardId] = amount;
        }

        return draft;
    }

    public int CountOf(string cardId)
    {
        int found;

        return counts.TryGetValue(cardId, out found) ? found : 0;
    }

    public int Total
    {
        get
        {
            int total = 0;

            foreach (KeyValuePair<string, int> entry in counts) total += entry.Value;

            return total;
        }
    }

    public int Distinct
    {
        get { return counts.Count; }
    }

    public bool Complete
    {
        get { return Total == Decklist.Size; }
    }

    public int Room
    {
        get { return Decklist.Size - Total; }
    }

    public bool Add(string cardId, out string trouble)
    {
        trouble = "";

        if (!Decklist.InPool(cardId))
        {
            trouble = "that card is not in the pool";
            return false;
        }

        if (Total >= Decklist.Size)
        {
            trouble = "the deck is already " + Decklist.Size + " cards";
            return false;
        }

        int held = CountOf(cardId);

        if (held >= Decklist.MaxCopies)
        {
            trouble = "no more than " + Decklist.MaxCopies + " copies of a card";
            return false;
        }

        counts[cardId] = held + 1;

        return true;
    }

    public bool Remove(string cardId)
    {
        int held = CountOf(cardId);
        if (held <= 0) return false;

        if (held == 1) counts.Remove(cardId);
        else counts[cardId] = held - 1;

        return true;
    }

    public void Clear()
    {
        counts.Clear();
    }

    public int Fill()
    {
        int added = 0;

        List<CardDefinition> pool = Cheapest();

        for (int pass = 1; pass <= Decklist.MaxCopies; pass++)
        {
            foreach (CardDefinition card in pool)
            {
                if (Total >= Decklist.Size) return added;
                if (CountOf(card.CardID) >= pass) continue;

                string trouble;
                if (Add(card.CardID, out trouble)) added++;
            }
        }

        return added;
    }

    public string Wire()
    {
        StringBuilder sb = new StringBuilder();

        foreach (CardDefinition card in Decklist.Pool())
        {
            int held = CountOf(card.CardID);
            if (held <= 0) continue;

            if (sb.Length > 0) sb.Append(',');

            sb.Append(card.CardID).Append(':').Append(held);
        }

        return sb.ToString();
    }

    public string Trouble
    {
        get
        {
            int total = Total;

            if (total == Decklist.Size) return "";

            if (total < Decklist.Size)
                return "add " + (Decklist.Size - total) + " more card" + (Decklist.Size - total == 1 ? "" : "s");

            return "remove " + (total - Decklist.Size) + " card" + (total - Decklist.Size == 1 ? "" : "s");
        }
    }

    public static int PoolCapacity()
    {
        return Decklist.Pool().Count * Decklist.MaxCopies;
    }

    public string Describe(string highlight, string filter)
    {
        StringBuilder sb = new StringBuilder();

        int total = Total;
        string trouble = Trouble;
        string wanted = string.IsNullOrEmpty(filter) ? "" : filter.Trim().ToLowerInvariant();

        sb.Append("<size=115%>Your deck   ")
          .Append(total == Decklist.Size ? "<color=#7FD98A>" : "<color=#FFB340>")
          .Append(total).Append('/').Append(Decklist.Size).Append("</color></size>\n");

        string howto = total >= Decklist.Size
            ? "right-click a card to take one out, then click another to put it in"
            : "click a card to add one, right-click to take one back";

        sb.Append("<size=13><color=#9AA0A6>")
          .Append(trouble.Length > 0 ? trouble : "ready to save")
          .Append("   -   ").Append(howto).Append("</color></size>\n");

        sb.Append("<size=13>").Append(Curve()).Append("</size>\n\n");

        int shown = 0;

        foreach (CardDefinition card in Cheapest())
        {
            string name = CardInfo.Pretty(card.name);

            if (wanted.Length > 0 && name.ToLowerInvariant().IndexOf(wanted) < 0) continue;

            shown++;

            int held = CountOf(card.CardID);
            bool lit = card.CardID == highlight;

            sb.Append("<link=\"").Append(card.CardID).Append("\">");
            if (lit) sb.Append("<mark=#ffffff22>");

            sb.Append(held > 0 ? "<color=#FFFFFF>" : "<color=#9AA0A6>");

            sb.Append("<pos=2%>").Append(card.cost);
            sb.Append("<pos=11%>").Append(name);
            sb.Append("<pos=58%><size=90%>").Append(Stats(card)).Append("</size>");
            sb.Append("<pos=72%><size=85%><color=#9AA0A6>").Append(Tag(card)).Append("</color></size>");
            sb.Append("<pos=93%>").Append(Copies(held));

            sb.Append("</color>");
            sb.Append(lit ? "</mark>" : "").Append("</link>\n");
        }

        if (shown == 0)
            sb.Append("<color=#FFB340>No card is called \"").Append(filter).Append("\".</color>\n");

        return sb.ToString();
    }

    public string Describe(string highlight)
    {
        return Describe(highlight, "");
    }

    private string Curve()
    {
        Dictionary<int, int> byCost = new Dictionary<int, int>();
        int most = 0;

        foreach (CardDefinition card in Decklist.Pool())
        {
            int held = CountOf(card.CardID);
            if (held <= 0) continue;

            int cost = card.cost;

            byCost[cost] = byCost.ContainsKey(cost) ? byCost[cost] + held : held;

            if (cost > most) most = cost;
        }

        if (most == 0) return "";

        StringBuilder sb = new StringBuilder("<color=#9AA0A6>curve</color>  ");

        for (int cost = 1; cost <= most; cost++)
        {
            int held;
            byCost.TryGetValue(cost, out held);

            sb.Append("<color=#9AA0A6>").Append(cost).Append("</color>")
              .Append(held > 0 ? "<color=#7FD98A>" : "<color=#5F6368>")
              .Append(':').Append(held).Append("</color>  ");
        }

        return sb.ToString();
    }

    private static string Copies(int held)
    {
        if (held <= 0) return "<color=#5F6368>-</color>";

        return "<color=#7FD98A>x" + held + "</color>";
    }

    private static string Stats(CardDefinition card)
    {
        CreatureCard creature = card as CreatureCard;

        return creature != null ? creature.strength + "/" + creature.health : "spell";
    }

    private static string Tag(CardDefinition card)
    {
        CreatureCard creature = card as CreatureCard;

        if (creature == null) return "";

        if (creature.hasTaunt) return "taunt";
        if (creature.hasCharge) return "charge";
        if (creature.hasShield) return "shield";
        if (creature.hasLifesteal) return "lifesteal";
        if (creature.hasDeathrattle) return "deathrattle";

        return "";
    }

    private static List<CardDefinition> Cheapest()
    {
        List<CardDefinition> sorted = new List<CardDefinition>(Decklist.Pool());

        sorted.Sort(delegate (CardDefinition left, CardDefinition right)
        {
            if (left.cost != right.cost) return left.cost.CompareTo(right.cost);

            return string.CompareOrdinal(left.CardID, right.CardID);
        });

        return sorted;
    }
}
