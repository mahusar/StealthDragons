using System.Globalization;

public class HostFeeOption : IServerOption
{
    public const decimal Minimum = 0.01m;

    public static decimal FeeXst { get; private set; }

    public string Key { get { return "fee"; } }

    public string Label { get { return "Host fee"; } }

    public string PromptText
    {
        get { return "host fee in XST per match (minimum " + Format(Minimum) + ", blank for none)"; }
    }

    public string DescribeCurrent()
    {
        if (FeeXst <= 0m) return "no host fee";

        decimal winnings = BetAmountOption.BetXst;
        string share = winnings > 0m
            ? "  (" + Format(decimal.Round(FeeXst / winnings * 100m, 2)) + "% of winnings)"
            : "";

        return Format(FeeXst) + " XST per match" + share;
    }

    public void ApplyDefault()
    {
        FeeXst = 0m;
    }

    public bool TryApply(string input, out string error)
    {
        error = null;

        if (string.IsNullOrEmpty(input) || input.Trim().Length == 0)
        {
            FeeXst = 0m;
            return true;
        }

        string cleaned = input.Trim().Replace(',', '.');

        decimal parsed;
        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            error = "'" + input.Trim() + "' is not a number.";
            return false;
        }

        if (parsed < 0m)
        {
            error = "The host fee cannot be negative.";
            return false;
        }

        if (parsed > 0m && parsed < Minimum)
        {
            error = "The smallest host fee is " + Format(Minimum) + " XST. Leave blank for no fee.";
            return false;
        }

        decimal bet = BetAmountOption.BetXst;
        if (parsed >= bet && bet > 0m)
        {
            error = "A fee of " + Format(parsed) + " XST would leave the winner with no more than their own " +
                    Format(bet) + " XST bet back. Keep the fee below the bet.";
            return false;
        }

        FeeXst = parsed;
        return true;
    }

    public string ToWire()
    {
        return "fee=" + Format(FeeXst);
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
