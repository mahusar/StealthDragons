using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

[Serializable]
public partial struct CardInfo
{
    public string cardID;
    public int amount;

    public CardInfo(CardDefinition card, int amount = 1)
    {
        cardID = card != null ? card.CardID : "";
        this.amount = amount;
    }

    public bool Known
    {
        get { return !string.IsNullOrEmpty(cardID) && CardDefinition.Cache.ContainsKey(cardID); }
    }

    public CardDefinition data
    {
        get
        {
            CardDefinition found;

            if (CardDefinition.Cache.TryGetValue(cardID ?? "", out found)) return found;

            throw new KeyNotFoundException("No card asset carries the id " + cardID +
                                           ". Check that it still lives under a Resources folder.");
        }
    }

    public Sprite image
    {
        get { return data.image; }
    }

    public string name
    {
        get { return data.name; }
    }

    public string displayName
    {
        get { return Pretty(data != null ? data.name : ""); }
    }

    public static string Pretty(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        int mark = raw.IndexOf('_');
        if (mark <= 0 || mark + 1 >= raw.Length) return raw;

        for (int i = 0; i < mark; i++)
            if (!char.IsDigit(raw[i])) return raw;

        return raw.Substring(mark + 1);
    }

    public string cost
    {
        get { return data.cost.ToString(); }
    }

    public string description
    {
        get { return data.description; }
    }

    public List<Target> acceptableTargets
    {
        get
        {
            CreatureCard creature = data as CreatureCard;

            return creature != null ? creature.acceptableTargets : new List<Target>();
        }
    }

    public override string ToString()
    {
        return amount > 1 ? cardID + " x" + amount : cardID;
    }
}

public class CardList : SyncList<CardInfo> { }
