using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;

public static class ServerSetup
{
    private const string SkipFlag = "-nosetup";
    private const string ForceFlag = "-forcesetup";
    private const string DropLogFlag = "-nosetuplog";
    private const string FeeAlias = "-hostfee";
    private const string RpcConfigFile = "rpc.conf";

    private static bool ran;
    private static Gate gate;

    public static bool WalletReady { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void Hold()
    {
        if (!Enabled()) return;

        gate = new Gate(Debug.unityLogger.logHandler);
        Debug.unityLogger.logHandler = gate;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (!Enabled()) return;

        try
        {
            ServerBanner.Print();
            Run();
        }
        finally
        {
            Release();
        }
    }

    private static bool Enabled()
    {
        return HasFlag(ForceFlag) || Utils.IsHeadless();
    }

    private static void Run()
    {
        if (ran) return;
        ran = true;

        WalletReady = HasWalletCredentials();
        if (!WalletReady)
        {
            Console.WriteLine("   no usable " + RpcConfigFile + " in " + Application.persistentDataPath);
            Console.WriteLine("   this server cannot take bets or pay out, so it will not start.");
            Console.WriteLine("   running as a different user than the one that owns rpc.conf does this.");
            Console.WriteLine();
            return;
        }

        ServerOptions.ApplyDefaults();

        if (ApplyCommandLine())
        {
            Announce("command line");
            return;
        }

        if (HasFlag(SkipFlag))
        {
            Announce("setup skipped");
            return;
        }

        if (!IsInteractive())
        {
            Announce("no console attached, default profile");
            return;
        }

        try
        {
            Prompt();
        }
        catch (Aborted)
        {
            Console.WriteLine();
            Console.WriteLine("   no input available — using the default profile.");
            ServerOptions.ApplyDefaults();
        }
        catch (Exception e)
        {
            Console.WriteLine("   setup failed (" + e.Message + ") — using the default profile.");
            ServerOptions.ApplyDefaults();
        }

        Announce(null);
    }

    private static void Prompt()
    {
        Console.WriteLine("   Dragonator setup");
        Console.WriteLine();
        Console.WriteLine("     1  no host fee");
        Console.WriteLine("     2  set a host fee and other options");
        Console.WriteLine();
        Console.Write("   select [1]: ");

        string choice = ReadLine();
        Console.WriteLine();

        if (choice == "2") RunCustom();
        else ServerOptions.ApplyDefaults();
    }

    private static void RunCustom()
    {
        foreach (IServerOption option in ServerOptions.All)
        {
            while (true)
            {
                Console.Write("   " + option.PromptText + " [" + option.DescribeCurrent() + "]: ");
                string input = ReadLine();

                string error;
                if (option.TryApply(input, out error)) break;

                Console.WriteLine("   " + error);
            }
        }

        Console.WriteLine();
    }

    private static bool HasWalletCredentials()
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, RpcConfigFile);
            if (!File.Exists(path)) return false;

            bool user = false;
            bool password = false;
            bool url = false;

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                int split = trimmed.IndexOf('=');
                if (split <= 0) continue;

                string key = trimmed.Substring(0, split).Trim().ToLowerInvariant();
                bool filled = trimmed.Substring(split + 1).Trim().Length > 0;

                if (key == "rpcuser") user = filled;
                else if (key == "rpcpassword") password = filled;
                else if (key == "rpcurl") url = filled;
            }

            return user && password && url;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsInteractive()
    {
        try
        {
            return !Console.IsInputRedirected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ReadLine()
    {
        string line = Console.ReadLine();
        if (line == null) throw new Aborted();

        return line.Trim();
    }

    private static bool ApplyCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        bool any = false;

        for (int i = 0; i + 1 < args.Length; i++)
        {
            IServerOption option = MatchFlag(args[i]);
            if (option == null) continue;

            string error;
            if (option.TryApply(args[i + 1], out error)) any = true;
            else Console.WriteLine("   " + args[i] + ": " + error);

            i++;
        }

        return any;
    }

    private static IServerOption MatchFlag(string arg)
    {
        if (string.IsNullOrEmpty(arg) || arg.Length < 2 || arg[0] != '-') return null;

        string key = string.Equals(arg, FeeAlias, StringComparison.OrdinalIgnoreCase)
            ? "fee"
            : arg.Substring(1).ToLowerInvariant();

        foreach (IServerOption option in ServerOptions.All)
            if (option.Key == key) return option;

        return null;
    }

    private static void Announce(string reason)
    {
        ServerOptions.MarkConfigured();

        string suffix = string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")";
        Console.WriteLine("   starting server — " + ServerOptions.DescribeAll() + suffix);
        Console.WriteLine();
    }

    private static void Release()
    {
        if (gate == null) return;

        Gate closing = gate;
        gate = null;
        closing.Flush(!HasFlag(DropLogFlag));
    }

    private static bool HasFlag(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private class Aborted : Exception
    {
    }

    private class Gate : ILogHandler
    {
        private readonly ILogHandler inner;
        private readonly List<LogType> types = new List<LogType>();
        private readonly List<string> lines = new List<string>();

        public Gate(ILogHandler inner)
        {
            this.inner = inner;
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            types.Add(logType);
            lines.Add(Compose(format, args));
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            types.Add(LogType.Exception);
            lines.Add(exception == null ? "" : exception.ToString());
        }

        public void Flush(bool replay)
        {
            Debug.unityLogger.logHandler = inner;
            if (!replay) return;

            for (int i = 0; i < lines.Count; i++)
            {
                switch (types[i])
                {
                    case LogType.Warning:
                        Debug.LogWarning(lines[i]);
                        break;
                    case LogType.Error:
                    case LogType.Exception:
                        Debug.LogError(lines[i]);
                        break;
                    case LogType.Assert:
                        Debug.LogAssertion(lines[i]);
                        break;
                    default:
                        Debug.Log(lines[i]);
                        break;
                }
            }
        }

        private static string Compose(string format, object[] args)
        {
            if (format == null) return "";
            if (args == null || args.Length == 0) return format;

            try
            {
                return string.Format(format, args);
            }
            catch (Exception)
            {
                return format;
            }
        }
    }
}
