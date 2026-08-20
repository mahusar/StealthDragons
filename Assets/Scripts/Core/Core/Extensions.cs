using System.Collections.Generic;
using System.Globalization;

public static class Extensions
{
    public static List<CardInfo> ToList(this CardList cards)
    {
        List<CardInfo> copy = new List<CardInfo>(cards.Count);

        for (int i = 0; i < cards.Count; i++) copy.Add(cards[i]);

        return copy;
    }

    public static bool CanTarget(this Target targetType, List<Target> targets)
    {
        return targets != null && targets.Contains(targetType);
    }

    public static int ToInt(this string text)
    {
        int value;

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
    }
}
