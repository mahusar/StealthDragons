using System.Text;
using UnityEngine;

public static class PlayerName
{
    public const string PlayerPrefsKey = "Name";
    public const string Default = "StealthDragon";
    public const int MaxLength = 24;

    private const string CommandLineFlag = "-name";

    public static string Resolve()
    {
        string fromCommandLine = FromCommandLine();
        if (!string.IsNullOrEmpty(fromCommandLine)) return fromCommandLine;

        return Sanitize(PlayerPrefs.GetString(PlayerPrefsKey, Default));
    }

    public static void Save(string name)
    {
        Remember(name);
        PlayerPrefs.Save();
    }

    public static void Remember(string name)
    {
        PlayerPrefs.SetString(PlayerPrefsKey, Sanitize(name));
    }

    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Default;

        StringBuilder clean = new StringBuilder(Mathf.Min(name.Length, MaxLength));
        foreach (char c in name)
        {
            if (clean.Length >= MaxLength) break;
            if (c == '<' || c == '>' || c == '\n' || c == '\r') continue;
            if (char.IsControl(c)) continue;
            clean.Append(c);
        }

        string result = clean.ToString().Trim();
        return result.Length == 0 ? Default : result;
    }

    private static string FromCommandLine()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == CommandLineFlag)
                return Sanitize(args[i + 1]);

        return null;
    }
}
