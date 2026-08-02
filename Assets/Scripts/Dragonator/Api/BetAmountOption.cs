using System.Globalization;

public class BetAmountOption : IServerOption
{
    public const decimal Minimum = 0.01m;
    public const decimal DefaultBet = 0.1m;

    public static decimal BetXst { get; private set; }

    public string Key { get { return "bet"; } }

    public string Label { get { return "Bet"; } }

    public string PromptText
    {
        get { return "bet in XST each player pays (minimum " + Format(Minimum) + ")"; }
    }

    public string DescribeCurrent()
    {
        return Format(BetXst) + " XST bet";
    }

    public void ApplyDefault()
    {
        BetXst = DefaultBet;
    }

    public bool TryApply(string input, out string error)
    {
        error = null;

        if (string.IsNullOrEmpty(input) || input.Trim().Length == 0) return true;

        string cleaned = input.Trim().Replace(',', '.');

        decimal parsed;
        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            error = "'" + input.Trim() + "' is not a number.";
            return false;
        }

        if (parsed < Minimum)
        {
            error = "The smallest bet is " + Format(Minimum) + " XST.";
            return false;
        }

        BetXst = parsed;
        return true;
    }

    public string ToWire()
    {
        return "bet=" + Format(BetXst);
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
