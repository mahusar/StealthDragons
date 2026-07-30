using System.Collections.Generic;
using System.Text;

public interface IServerOption
{
    string Key { get; }
    string Label { get; }
    string PromptText { get; }
    string DescribeCurrent();
    void ApplyDefault();
    bool TryApply(string input, out string error);
    string ToWire();
}

public static class ServerOptions
{
    private static readonly List<IServerOption> registered = new List<IServerOption>();
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

    public static void Register(IServerOption option)
    {
        if (option == null) return;
        Bootstrap();

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

    public static string DescribeAll()
    {
        Bootstrap();
        StringBuilder sb = new StringBuilder();

        foreach (IServerOption option in registered)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(option.DescribeCurrent());
        }

        return sb.ToString();
    }

    public static string ToWire()
    {
        Bootstrap();
        StringBuilder sb = new StringBuilder();

        foreach (IServerOption option in registered)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(option.ToWire());
        }

        return sb.ToString();
    }
}
