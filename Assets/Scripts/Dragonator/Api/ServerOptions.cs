using System.Collections.Generic;
using System.Text;

public static class ServerOptions
{
    private static readonly List<IServerOption> registered = new List<IServerOption>();
    private static readonly HashSet<string> external = new HashSet<string>();
    private static bool bootstrapped;

    public static bool Configured { get; private set; }

    public static List<IServerOption> All
    {
        get
        {
            Bootstrap();
            return registered;
        }
    }

    public static List<IServerOption> External
    {
        get
        {
            Bootstrap();

            List<IServerOption> found = new List<IServerOption>();
            foreach (IServerOption option in registered)
                if (external.Contains(option.Key)) found.Add(option);

            return found;
        }
    }

    public static void Register(IServerOption option)
    {
        if (option == null) return;
        Bootstrap();

        external.Add(option.Key);

        for (int i = 0; i < registered.Count; i++)
        {
            if (registered[i].Key != option.Key) continue;
            registered[i] = option;
            return;
        }

        registered.Add(option);
    }

    private static void Bootstrap()
    {
        if (bootstrapped) return;
        bootstrapped = true;
        registered.Add(new BetAmountOption());
        registered.Add(new HostFeeOption());
    }

    public static void ApplyDefaults()
    {
        Bootstrap();
        foreach (IServerOption option in registered) option.ApplyDefault();
    }

    public static void MarkConfigured()
    {
        Configured = true;
    }

    public static string DescribeCore()
    {
        Bootstrap();
        StringBuilder sb = new StringBuilder();

        foreach (IServerOption option in registered)
        {
            if (external.Contains(option.Key)) continue;

            if (sb.Length > 0) sb.Append(", ");
            sb.Append(Describe(option));
        }

        return sb.ToString();
    }

    public static List<string> DescribeExternal()
    {
        List<IServerOption> shown = new List<IServerOption>();
        foreach (IServerOption option in External)
            if (ShouldShow(option)) shown.Add(option);

        List<string> lines = new List<string>();
        int width = 0;

        foreach (IServerOption option in shown)
        {
            int length = Label(option).Length;
            if (length > width) width = length;
        }

        foreach (IServerOption option in shown)
            lines.Add(Label(option).PadRight(width + 3) + Describe(option));

        return lines;
    }

    public static int OrderOf(IServerOption option)
    {
        IServerOptionListing listing = option as IServerOptionListing;
        if (listing == null) return 0;

        try
        {
            return listing.Order;
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    public static bool ShouldAsk(IServerOption option)
    {
        IServerOptionListing listing = option as IServerOptionListing;
        if (listing == null) return true;

        try
        {
            return listing.Ask;
        }
        catch (System.Exception)
        {
            return true;
        }
    }

    public static bool ShouldShow(IServerOption option)
    {
        IServerOptionListing listing = option as IServerOptionListing;
        if (listing == null) return true;

        try
        {
            return listing.Show;
        }
        catch (System.Exception)
        {
            return true;
        }
    }

    public static string ToWire()
    {
        Bootstrap();
        StringBuilder sb = new StringBuilder();

        foreach (IServerOption option in registered)
        {
            string pair;
            try
            {
                pair = option.ToWire();
            }
            catch (System.Exception)
            {
                continue;
            }

            if (string.IsNullOrEmpty(pair)) continue;

            if (sb.Length > 0) sb.Append(';');
            sb.Append(pair);
        }

        return sb.ToString();
    }

    private static string Label(IServerOption option)
    {
        try
        {
            return option.Label ?? option.Key ?? "add-on";
        }
        catch (System.Exception)
        {
            return "add-on";
        }
    }

    private static string Describe(IServerOption option)
    {
        try
        {
            return option.DescribeCurrent() ?? "";
        }
        catch (System.Exception e)
        {
            return "unavailable (" + e.GetType().Name + ")";
        }
    }
}
