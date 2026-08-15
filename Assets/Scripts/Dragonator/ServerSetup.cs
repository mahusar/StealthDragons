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
    private const string StartPrefix = "   starting server - ";

    private static bool ran;
    private static Gate gate;

    public static bool Ready { get; private set; }

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
            AddonLoader.EnsureLoaded();
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

        ServerOptions.ApplyDefaults();

        string reason = Resolve();

        if (!Verify())
        {
            Ready = false;
            return;
        }

        Ready = true;
        Announce(reason);
    }

    private static bool Verify()
    {
        foreach (IServerWallet wallet in AddonLoader.Wallets)
        {
            if (!Required(wallet)) continue;

            Console.Write("   checking " + Describe(wallet) + "... ");

            string problem;
            bool ok;

            try
            {
                ok = wallet.Check(out problem);
            }
            catch (Exception e)
            {
                ok = false;
                problem = e.Message;
            }

            if (ok)
            {
                Console.WriteLine(string.IsNullOrEmpty(problem) ? "ok" : problem);
                continue;
            }

            Console.WriteLine("FAILED");
            Console.WriteLine("     " + (string.IsNullOrEmpty(problem) ? "no detail" : problem));
            Console.WriteLine();

            if (!IsInteractive() || HasFlag(SkipFlag))
            {
                Console.WriteLine("   " + Named(wallet) + " cannot reach " + Describe(wallet) + ", so it will not start.");
                Console.WriteLine("   fix it, or start a free server instead.");
                Console.WriteLine();
                return false;
            }

            Console.Write("   run as a free server instead? [y]: ");

            string answer;
            try
            {
                answer = ReadLine();
            }
            catch (Aborted)
            {
                answer = "y";
            }

            Console.WriteLine();

            if (answer.Length > 0 && !answer.StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("   not starting.");
                Console.WriteLine();
                return false;
            }

            UseFree();
            return true;
        }

        return true;
    }

    private static void UseFree()
    {
        foreach (IServerWallet wallet in AddonLoader.Wallets)
        {
            try
            {
                wallet.UseFree();
            }
            catch (Exception e)
            {
                Console.WriteLine("   " + Named(wallet) + " could not be switched to free (" + e.Message + ").");
            }
        }
    }

    private static bool Chargeable()
    {
        foreach (IServerWallet wallet in AddonLoader.Wallets)
            if (Required(wallet)) return true;

        return false;
    }

    private static bool Required(IServerWallet wallet)
    {
        try
        {
            return wallet.Required;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Describe(IServerWallet wallet)
    {
        try
        {
            return wallet.Needs ?? "its wallet";
        }
        catch (Exception)
        {
            return "its wallet";
        }
    }

    private static string Named(IServerWallet wallet)
    {
        try
        {
            return wallet.Name ?? "an add-on";
        }
        catch (Exception)
        {
            return "an add-on";
        }
    }

    private static string Resolve()
    {
        if (ApplyCommandLine()) return "command line";

        if (HasFlag(SkipFlag)) return "setup skipped";

        if (!IsInteractive()) return "no console attached, default profile";

        try
        {
            Prompt();
        }
        catch (Aborted)
        {
            Console.WriteLine();
            Console.WriteLine("   no input available - using the default profile.");
            ServerOptions.ApplyDefaults();
        }
        catch (Exception e)
        {
            Console.WriteLine("   setup failed (" + e.Message + ") - using the default profile.");
            ServerOptions.ApplyDefaults();
        }

        return null;
    }

    private static void Prompt()
    {
        if (!AnyToAsk()) return;

        Console.WriteLine("   Dragonator setup");
        Console.WriteLine();
        Console.WriteLine("     1  free to play");

        if (AddonLoader.Wallets.Count == 0)
        {
            Console.WriteLine("     2  setup add-ons");
        }
        else
        {
            Console.WriteLine("     2  setup add-ons:");

            int width = 0;
            foreach (IServerWallet wallet in AddonLoader.Wallets)
            {
                int length = Named(wallet).Length;
                if (length > width) width = length;
            }

            foreach (IServerWallet wallet in AddonLoader.Wallets)
                Console.WriteLine("          " + Named(wallet).PadRight(width) + " - " + Describe(wallet));
        }

        Console.WriteLine();
        Console.Write("   select [1]: ");

        string choice = ReadLine();
        Console.WriteLine();

        if (choice == "2")
        {
            RunCustom();
            return;
        }

        UseFree();
    }

    private static bool AnyToAsk()
    {
        foreach (IServerOption option in ServerOptions.All)
            if (ServerOptions.ShouldAsk(option)) return true;

        return false;
    }

    private static void RunCustom()
    {
        foreach (IServerOption option in ServerOptions.All)
        {
            if (!ServerOptions.ShouldAsk(option)) continue;

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

        List<string> external = ServerOptions.DescribeExternal();

        string core = ServerOptions.DescribeCore();
        if (core.Length == 0) core = Chargeable() ? "add-ons active" : "free to play";

        Console.WriteLine(StartPrefix + core + suffix);

        string indent = new string(' ', StartPrefix.Length);
        foreach (string line in external)
            Console.WriteLine(indent + line);

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
