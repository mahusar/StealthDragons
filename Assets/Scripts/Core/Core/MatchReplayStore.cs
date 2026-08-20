using System;
using System.IO;
using UnityEngine;

public static class MatchReplayStore
{
    public const string Folder = "replays";
    public const int MaxBytes = 512 * 1024;

    public static readonly string Separator = ((char)9).ToString();

    private static readonly string NewLine = ((char)10).ToString();

    private static readonly string Return = ((char)13).ToString();

    public static string FolderPath()
    {
        return Path.Combine(Application.persistentDataPath, Folder);
    }

    public static string PathFor(string digestHex)
    {
        return Path.Combine(FolderPath(), digestHex + ".txt");
    }

    public static bool Save(string digestHex, string canonical)
    {
        if (!Usable(digestHex) || string.IsNullOrEmpty(canonical)) return false;

        try
        {
            Directory.CreateDirectory(FolderPath());
            File.WriteAllText(PathFor(digestHex), canonical);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"MatchReplayStore: {digestHex} could not be saved ({e.GetType().Name}: {e.Message}).");
            return false;
        }
    }

    public static string Lookup(string digestHex)
    {
        if (!Usable(digestHex)) return "";

        try
        {
            string path = PathFor(digestHex);
            if (!File.Exists(path)) return "";

            FileInfo info = new FileInfo(path);
            if (info.Length > MaxBytes)
            {
                Debug.LogWarning($"MatchReplayStore: {digestHex} is {info.Length} bytes and was not served.");
                return "";
            }

            return File.ReadAllText(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"MatchReplayStore: {digestHex} could not be read ({e.GetType().Name}).");
            return "";
        }
    }

    public static string Wire(string digestHex)
    {
        string canonical = Lookup(digestHex);

        if (string.IsNullOrEmpty(canonical)) return "";

        return canonical.Replace(Return, "").Replace(NewLine, Separator);
    }

    private static bool Usable(string digestHex)
    {
        if (string.IsNullOrEmpty(digestHex)) return false;
        if (digestHex.Length != 64) return false;

        foreach (char c in digestHex)
        {
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!hex) return false;
        }

        return true;
    }
}
