using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Mirror;
using Newtonsoft.Json.Linq;
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

    private const float ProbeTimeoutMs = 400f;
    private const int RpcTimeoutMs = 1200;

    private const string RpcConfigFile = "rpc.conf";

    private const string OnionFile = "onion.txt";
    private const string OnionFlag = "-onion";
    private const string OnionEnvVar = "DRAGONATOR_ONION";
    private const string OnionSuffix = ".onion";
    private const string OnionPlaceholder = "youronion.onion";

    private static bool printed;

    private static readonly string[] OnionTemplate =
    {
        "# Your hidden service address goes on the line below, on its own.",
        "# Read it with: sudo cat /var/lib/tor/hidden_service/hostname",
        OnionPlaceholder
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Print()
    {
        if (printed) return;
        if (!HasFlag(ForceFlag) && !Utils.IsHeadless()) return;
        if (HasFlag(SkipFlag)) return;

        printed = true;

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

        string torEndpoint = TorConfig.SocksHost + ":" + TorConfig.SocksPort;
        bool torUp = Reachable(TorConfig.SocksHost, TorConfig.SocksPort);
        OnionStatus onion = ProbeOnion();
        StealthStatus stealth = ProbeStealth();

        Row(sb, colour, "tor", torUp ? $"running   {torEndpoint}" : $"NOT DETECTED   nothing listening on {torEndpoint}");
        Row(sb, colour, "onion", onion.Line);
        Hints(sb, onion.Hints);
        Row(sb, colour, "stealth", stealth.Line);
        Row(sb, colour, "wallet", stealth.WalletLine);
        Hints(sb, stealth.WalletHints);
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

    private static void Hints(StringBuilder sb, List<string> lines)
    {
        foreach (string line in lines)
            sb.AppendLine(new string(' ', 12) + line);
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

    private static bool Reachable(string host, int port)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult pending = client.BeginConnect(host, port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(ProbeTimeoutMs))) return false;

                client.EndConnect(pending);
                return client.Connected;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private class StealthStatus
    {
        public string Line;
        public string WalletLine = "unknown   no reachable stealthd to ask";
        public readonly List<string> WalletHints = new List<string>();
    }

    private static StealthStatus ProbeStealth()
    {
        StealthStatus status = new StealthStatus();
        string path = Path.Combine(Application.persistentDataPath, RpcConfigFile);
        string url = null;
        string user = null;
        string password = null;

        try
        {
            if (!File.Exists(path))
            {
                status.Line = $"NOT CONFIGURED   no {RpcConfigFile} in {Application.persistentDataPath}";
                return status;
            }

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                int split = trimmed.IndexOf('=');
                if (split <= 0) continue;

                string key = trimmed.Substring(0, split).Trim().ToLowerInvariant();
                string value = trimmed.Substring(split + 1).Trim();

                if (key == "rpcurl") url = value;
                else if (key == "rpcuser") user = value;
                else if (key == "rpcpassword") password = value;
            }
        }
        catch (Exception e)
        {
            status.Line = $"unreadable   ({RpcConfigFile}: {e.Message})";
            return status;
        }

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            status.Line = $"INCOMPLETE   {RpcConfigFile} needs rpcuser, rpcpassword and rpcurl";
            return status;
        }

        string host;
        int port;
        if (!TryParseEndpoint(url, out host, out port))
        {
            status.Line = $"BAD rpcurl   cannot parse '{url}'";
            return status;
        }

        if (!Reachable(host, port))
        {
            status.Line = $"NOT REACHABLE   nothing listening on {host}:{port}";
            return status;
        }

        status.Line = $"connected   {host}:{port}";
        DescribeWallet(url, user, password, status);
        return status;
    }

    private static void DescribeWallet(string url, string user, string password, StealthStatus status)
    {
        RpcReply reply = Rpc(url, user, password, "getinfo");
        if (reply.Result == null)
        {
            RpcReply fallback = Rpc(url, user, password, "getwalletinfo");
            if (fallback.Result != null || fallback.Unauthorized) reply = fallback;
        }

        if (reply.Result == null)
        {
            status.WalletLine = "unknown   " + reply.Failure;
            if (reply.Unauthorized)
                status.WalletHints.Add("rpcuser and rpcpassword in rpc.conf must match StealthCoin.conf");

            return;
        }

        JToken until = reply.Result["unlocked_until"];
        if (until == null)
        {
            status.WalletLine = "unencrypted   no passphrase set, payouts will work";
            return;
        }

        long deadline;
        try
        {
            deadline = until.Value<long>();
        }
        catch (Exception)
        {
            status.WalletLine = "unknown   could not read unlocked_until";
            return;
        }

        if (deadline <= 0)
        {
            status.WalletLine = "LOCKED   payouts and refunds WILL FAIL";
            status.WalletHints.Add("unlock it: walletpassphrase \"<passphrase>\" <seconds>");
            return;
        }

        status.WalletLine = "unlocked   payouts enabled" + DescribeRemaining(deadline);
    }

    private static string DescribeRemaining(long unixDeadline)
    {
        try
        {
            TimeSpan left = DateTimeOffset.FromUnixTimeSeconds(unixDeadline) - DateTimeOffset.UtcNow;
            if (left.TotalSeconds <= 0) return "";
            if (left.TotalDays >= 90) return "";

            if (left.TotalHours >= 1)
                return $"   (relocks in {(int)left.TotalHours}h {left.Minutes}m)";

            return $"   (relocks in {(int)left.TotalMinutes}m)";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private class RpcReply
    {
        public JObject Result;
        public string Failure = "no reply from stealthd";
        public bool Unauthorized;
    }

    private static RpcReply Rpc(string url, string user, string password, string method)
    {
        RpcReply reply = new RpcReply();
        string body = "{\"jsonrpc\":\"1.0\",\"id\":\"banner\",\"method\":\"" + method + "\",\"params\":[]}";

        int code;
        string transport;
        string response = PostJson(url, user, password, body, out code, out transport);

        if (transport != null)
        {
            reply.Failure = "stealthd did not answer (" + Shorten(transport) + ")";
            return reply;
        }

        if (code == 401)
        {
            reply.Unauthorized = true;
            reply.Failure = "stealthd rejected the credentials in " + RpcConfigFile;
            return reply;
        }

        try
        {
            JObject parsed = JObject.Parse(response);
            JToken error = parsed["error"];

            if (error != null && error.Type != JTokenType.Null)
            {
                JToken message = error["message"];
                reply.Failure = method + " failed: " +
                                Shorten(message != null ? message.ToString() : error.ToString());
                return reply;
            }

            reply.Result = parsed["result"] as JObject;
            if (reply.Result == null) reply.Failure = method + " returned no result object";

            return reply;
        }
        catch (Exception)
        {
            reply.Failure = code == 200
                ? "stealthd sent a reply that is not JSON"
                : "stealthd answered HTTP " + code;

            return reply;
        }
    }

    private static string Shorten(string text)
    {
        if (string.IsNullOrEmpty(text)) return "no detail";

        string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 48 ? flat : flat.Substring(0, 45) + "...";
    }

    private static string PostJson(string url, string user, string password, string body,
                                   out int code, out string transport)
    {
        code = 0;
        transport = null;

        try
        {
            Uri uri = new Uri(url);
            string path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
            byte[] payload = Encoding.UTF8.GetBytes(body);

            StringBuilder head = new StringBuilder();
            head.Append("POST ").Append(path).Append(" HTTP/1.1\r\n");
            head.Append("Host: ").Append(uri.Host).Append(':').Append(uri.Port).Append("\r\n");
            head.Append("Authorization: Basic ")
                .Append(Convert.ToBase64String(Encoding.ASCII.GetBytes(user + ":" + password)))
                .Append("\r\n");
            head.Append("Content-Type: application/json\r\n");
            head.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
            head.Append("Connection: close\r\n\r\n");

            using (TcpClient client = new TcpClient())
            {
                IAsyncResult pending = client.BeginConnect(uri.Host, uri.Port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(RpcTimeoutMs))
                {
                    transport = "connect timed out";
                    return null;
                }

                client.EndConnect(pending);
                client.SendTimeout = RpcTimeoutMs;
                client.ReceiveTimeout = RpcTimeoutMs;

                using (NetworkStream stream = client.GetStream())
                using (MemoryStream received = new MemoryStream())
                {
                    byte[] request = Encoding.ASCII.GetBytes(head.ToString());
                    stream.Write(request, 0, request.Length);
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush();

                    byte[] chunk = new byte[4096];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                        received.Write(chunk, 0, read);

                    return SplitResponse(Encoding.UTF8.GetString(received.ToArray()), out code);
                }
            }
        }
        catch (Exception e)
        {
            transport = e.Message;
            return null;
        }
    }

    private static string SplitResponse(string raw, out int code)
    {
        code = 0;
        if (string.IsNullOrEmpty(raw)) return null;

        int blank = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        string header = blank < 0 ? raw : raw.Substring(0, blank);
        string body = blank < 0 ? "" : raw.Substring(blank + 4);

        int endOfStatus = header.IndexOf("\r\n", StringComparison.Ordinal);
        string status = endOfStatus < 0 ? header : header.Substring(0, endOfStatus);

        string[] parts = status.Split(' ');
        if (parts.Length >= 2) int.TryParse(parts[1], out code);

        return body;
    }

    private static bool TryParseEndpoint(string url, out string host, out int port)
    {
        host = null;
        port = 0;

        try
        {
            Uri uri = new Uri(url);
            host = uri.Host;
            port = uri.Port;
            return !string.IsNullOrEmpty(host) && port > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string OnionFilePath()
    {
        return Path.Combine(Application.persistentDataPath, OnionFile);
    }

    private class OnionStatus
    {
        public string Line;
        public readonly List<string> Hints = new List<string>();
    }

    private static OnionStatus ProbeOnion()
    {
        OnionStatus status = new OnionStatus();

        string flag = FlagValue(OnionFlag);
        if (IsOnionAddress(flag))
        {
            status.Line = flag.Trim();
            return status;
        }

        try
        {
            string fromEnv = Environment.GetEnvironmentVariable(OnionEnvVar);
            if (IsOnionAddress(fromEnv))
            {
                status.Line = fromEnv.Trim();
                return status;
            }
        }
        catch (Exception)
        {
        }

        string path = OnionFilePath();
        EnsureOnionFile(path);

        string saved = FirstLine(path);
        if (IsOnionAddress(saved) && !string.Equals(saved, OnionPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            status.Line = saved;
            return status;
        }

        status.Line = "not set   put your address in " + OnionFile + ", in the data path below";
        return status;
    }

    private static void EnsureOnionFile(string path)
    {
        try
        {
            if (File.Exists(path)) return;

            File.WriteAllLines(path, OnionTemplate);
        }
        catch (Exception)
        {
        }
    }

    private static bool IsOnionAddress(string value)
    {
        return !string.IsNullOrEmpty(value) &&
               value.Trim().EndsWith(OnionSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstLine(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                return trimmed;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static string FlagValue(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];

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
