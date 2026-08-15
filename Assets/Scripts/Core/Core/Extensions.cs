// This class adds functions to built-in types.
using UnityEngine;
using System.Collections.Generic;
using Mirror;

public static class Extensions
{
    // Converts CardInfo SyncLists to regular Lists
    public static List<CardInfo> ToList(this SyncListCard card)
    {
        // Create a new empty list that we can then convert into a copy of our SyncList
        List<CardInfo> cardTemp = new List<CardInfo>();
        for (int i = 0; i < card.Count; ++i)
        {
            cardTemp.Add(card[i]);
        }
        return cardTemp;
    }

    public static bool CanTarget(this Target targetType, List<Target> targets)
    {
        // Whether or not this is an entity we can actually target
        bool canTarget = false;

        // Loop through each target in our TargetList and see if any of them match our targetType
        foreach (Target currentTarget in targets)
        {
            // If one of the targets in our TargetList (currentTarget) is equal to our entity's targetType, then we canTarget it.
            if (currentTarget == targetType)
            {
                canTarget = true;
            }
        }
        return canTarget;
    }

    public static int ToInt(this string text)
    {
        return int.Parse(text);
    }
}
