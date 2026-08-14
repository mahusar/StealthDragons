using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class ServerDirectory
{
    public const int MaxListings = 200;

    private const int LabelLength = 56;
    private const string Suffix = ".onion";

    private static readonly List<IServerDirectory> sources = new List<IServerDirectory>();
    private static readonly List<string> seeds = new List<string>();

    public static int SourceCount
    {
        get { return sources.Count; }
    }

    public static void Register(IServerDirectory source)
    {
        if (source == null) return;
        if (sources.Contains(source)) return;

        sources.Add(source);
    }

    public static void AddSeed(string entry)
    {
        string clean = Clean(entry);
        if (clean == null) return;
        if (seeds.Contains(clean)) return;

        seeds.Add(clean);
    }

    public static List<string> All()
    {
        List<string> found = new List<string>();
        HashSet<string> seen = new HashSet<string>();

        foreach (string seed in seeds) Add(found, seen, seed);

        foreach (IServerDirectory source in sources)
        {
            List<string> listings;

            try
            {
                listings = source.Listings;
            }
            catch (Exception)
            {
                continue;
            }

            if (listings == null) continue;

            foreach (string listing in listings)
            {
                Add(found, seen, listing);
                if (found.Count >= MaxListings) return found;
            }
        }

        return found;
    }

    public static string ToWire()
    {
        StringBuilder sb = new StringBuilder();

        foreach (string entry in All())
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(entry);
        }

        return sb.ToString();
    }

    public static string StatusLine
    {
        get
        {
            int count = All().Count;
            string tail = count + (count == 1 ? " server" : " servers");

            if (sources.Count == 0)
                return seeds.Count == 0 ? "off" : tail + " (seeded)";

            List<string> names = new List<string>();

            foreach (IServerDirectory source in sources)
            {
                string described = Describe(source);
                if (described.Length > 0) names.Add(described);
            }

            return names.Count == 0 ? tail : tail + "   " + string.Join(", ", names.ToArray());
        }
    }

    private static string Describe(IServerDirectory source)
    {
        string name;
        string status;

        try
        {
            name = source.Name;
        }
        catch (Exception)
        {
            name = null;
        }

        try
        {
            status = source.Status;
        }
        catch (Exception)
        {
            status = null;
        }

        if (string.IsNullOrEmpty(name)) name = "add-on";
        if (string.IsNullOrEmpty(status)) return name;

        return name + " " + status;
    }

    private static void Add(List<string> found, HashSet<string> seen, string entry)
    {
        if (found.Count >= MaxListings) return;

        string clean = Clean(entry);
        if (clean == null) return;
        if (!seen.Add(clean)) return;

        found.Add(clean);
    }

    private static string Clean(string entry)
    {
        if (string.IsNullOrEmpty(entry)) return null;

        string trimmed = entry.Trim().ToLowerInvariant();

        int colon = trimmed.LastIndexOf(':');
        if (colon <= 0) return null;

        int port;
        if (!int.TryParse(trimmed.Substring(colon + 1), NumberStyles.Integer,
                          CultureInfo.InvariantCulture, out port)) return null;

        if (port <= 0 || port > 65535) return null;

        string host = trimmed.Substring(0, colon);
        if (!host.EndsWith(Suffix, StringComparison.Ordinal)) return null;

        string label = host.Substring(0, host.Length - Suffix.Length);
        if (label.Length != LabelLength) return null;

        for (int i = 0; i < label.Length; i++)
        {
            char c = label[i];
            if ((c < 'a' || c > 'z') && (c < '2' || c > '7')) return null;
        }

        return host + ":" + port.ToString(CultureInfo.InvariantCulture);
    }
}
