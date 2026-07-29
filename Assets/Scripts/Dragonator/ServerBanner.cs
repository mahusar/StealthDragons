using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Mirror;
using UnityEngine;

public static class ServerBanner
{
    private const string IconFile = "dragon.txt";
    private const string ColourIconFile = "dragon.ans";

    private const string SkipFlag = "-nobanner";
    private const string NoIconFlag = "-nobannericon";
    private const string FullColourFlag = "-bannercolour";
    private const string FullColourFlagUs = "-bannercolor";
    private const string NoColourFlag = "-nobannercolour";
    private const string NoColourFlagUs = "-nobannercolor";
    private const string ForceFlag = "-forcebanner";

    private const string Green = "\u001b[38;2;45;190;145m";
    private const string Label = "\u001b[38;2;120;140;135m";
    private const string Reset = "\u001b[0m";

    private const float TorProbeTimeoutMs = 400f;

    private static readonly string[] OnionHostnamePaths =
    {
        "/var/lib/tor/hidden_service/hostname",
        "/var/lib/tor/dragonator/hostname",
        "/var/lib/tor/stealthdragons/hostname"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Print()
    {
        if (!HasFlag(ForceFlag) && !Utils.IsHeadless()) return;
        if (HasFlag(SkipFlag)) return;

        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception) { }

        bool colour = UseColour();
        StringBuilder sb = new StringBuilder();
        sb.AppendLine();

        if (!HasFlag(NoIconFlag))
        {
            string icon = ReadArt(colour ? ColourIconFile : IconFile);
            if (icon == null) icon = ReadArt(IconFile);

            if (icon != null)
            {
                sb.AppendLine(icon.TrimEnd('\r', '\n'));
                sb.AppendLine();
            }
        }

        string title = $"DRAGONATOR  {Application.version}";
        sb.AppendLine(colour ? Green + Centre(title) + Reset : Centre(title));
        sb.AppendLine();

        string onion = ReadOnionHostname();
        string torEndpoint = TorConfig.SocksHost + ":" + TorConfig.SocksPort;
        bool torUp = TorReachable();

        Row(sb, colour, "tor", torUp ? $"running   {torEndpoint}" : $"NOT DETECTED   nothing listening on {torEndpoint}");
        Row(sb, colour, "onion", onion ?? "unavailable   (hostname file not readable by this user)");
        Row(sb, colour, "ports", $"game {TorConfig.GamePort}   matchmaker {TorConfig.MatchmakerPort}");
        Row(sb, colour, "data", Application.persistentDataPath);
        sb.AppendLine();

        Console.WriteLine(sb.ToString());
    }

    private const int BannerWidth = 70;

    private static string Centre(string text)
    {
        int pad = (BannerWidth - text.Length) / 2;
        return pad > 0 ? new string(' ', pad) + text : text;
    }

    private static void Row(StringBuilder sb, bool colour, string label, string value)
    {
        string padded = label.PadRight(9);
        sb.AppendLine(colour
            ? $"   {Label}{padded}{Reset}{value}"
            : $"   {padded}{value}");
    }

    private static bool UseColour()
    {
        if (HasFlag(NoColourFlag) || HasFlag(NoColourFlagUs)) return false;

        try
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return false;
            string term = Environment.GetEnvironmentVariable("TERM");
            if (term == "dumb") return false;
        }
        catch (Exception) { }

        return true;
    }

    private static bool TorReachable()
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult pending = client.BeginConnect(TorConfig.SocksHost, TorConfig.SocksPort, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(TorProbeTimeoutMs))) return false;

                client.EndConnect(pending);
                return client.Connected;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ReadOnionHostname()
    {
        foreach (string path in OnionHostnamePaths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                string hostname = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(hostname)) return hostname;
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    private static string ReadArt(string fileName)
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ServerBanner] {fileName} not found at {path}.");
                return null;
            }
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ServerBanner] Could not read {fileName}: {e.Message}");
            return null;
        }
    }

    private static bool HasFlag(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
