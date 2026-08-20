using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEngine;

public static class AddonLoader
{
    private const string AddonFolder = "Addons";
    private const string SkipFlag = "-noaddons";
    private const string ForceFlag = "-forceaddons";
    private const string ReadmeFile = "README.txt";

    private static readonly string[] ReadmeTemplate =
    {
        "Drop server add-on .dll files in this folder, one per add-on.",
        "They are loaded when Dragonator starts, and each one may add its own",
        "setup question and its own line to the server info players see.",
        "",
        "Delete a .dll to uninstall it. Start with -noaddons to load none of them.",
        "",
        "An add-on runs inside the server process with full access to the wallet.",
        "Only install ones you trust.",
    };

    private static readonly List<string> names = new List<string>();
    private static readonly List<string> failures = new List<string>();

    private static bool loaded;
    private static IMatchEscrow escrow;
    private static string escrowOwner;
    private static IMatchWitness witness;
    private static string witnessOwner;
    private static IMatchBot bots;
    private static string botsOwner;

    private static readonly List<IServerWallet> wallets = new List<IServerWallet>();

    public static List<IServerWallet> Wallets
    {
        get
        {
            EnsureLoaded();
            return wallets;
        }
    }

    public static List<string> Hints
    {
        get { return failures; }
    }

    public static IMatchEscrow Escrow
    {
        get
        {
            EnsureLoaded();
            return escrow;
        }
    }

    public static IMatchWitness Witness
    {
        get
        {
            EnsureLoaded();
            return witness;
        }
    }

    public static IMatchBot Bots
    {
        get
        {
            EnsureLoaded();
            return bots;
        }
    }

    public static string Installed
    {
        get
        {
            EnsureLoaded();

            string joined = "";

            foreach (string name in names)
            {
                string clean = name.Replace(';', ' ').Replace('=', ' ').Replace(',', ' ').Trim();
                if (clean.Length == 0) continue;

                if (joined.Length > 0) joined += ",";
                joined += clean;
            }

            return joined;
        }
    }

    public static string StatusLine
    {
        get
        {
            if (names.Count == 0 && failures.Count == 0) return "none";

            if (names.Count == 0) return Plural(failures.Count, "failed");

            string line = Plural(names.Count, "loaded") + "   " + string.Join(", ", names.ToArray());
            if (failures.Count > 0) line += "   (" + Plural(failures.Count, "failed") + ")";

            return line;
        }
    }

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        if (!Enabled()) return;
        if (HasFlag(SkipFlag)) return;

        string folder = Path.Combine(Application.persistentDataPath, AddonFolder);

        string[] files;
        try
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                WriteReadme(folder);
                return;
            }

            WriteReadme(folder);
            files = Directory.GetFiles(folder, "*.dll");
        }
        catch (Exception e)
        {
            failures.Add(AddonFolder + " is unreadable: " + Shorten(e.Message));
            return;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (string file in files) Load(file);
    }

    private static void Load(string file)
    {
        string shown = Path.GetFileName(file);

        try
        {
            Assembly assembly = Assembly.LoadFrom(file);

            int needs = NeedsApi(assembly);
            if (needs > DragonatorApi.Version)
            {
                failures.Add(shown + ": needs Dragonator API " + needs + ", this build has " +
                             DragonatorApi.Version + " - update Dragonator");
                return;
            }

            Type[] types = TypesOf(assembly, shown);

            int added = RegisterOptions(types, shown);
            bool escrowAdded = RegisterEscrow(types, shown);
            bool witnessAdded = RegisterWitness(types, shown);
            bool botsAdded = RegisterBots(types, shown);
            int walletsAdded = RegisterWallets(types, shown);
            int directoriesAdded = RegisterDirectories(types, shown);

            if (added == 0 && !escrowAdded && !witnessAdded && !botsAdded &&
                walletsAdded == 0 && directoriesAdded == 0)
            {
                failures.Add(shown + ": nothing Dragonator can use inside it");
                return;
            }

            names.Add(Describe(assembly));
        }
        catch (Exception e)
        {
            failures.Add(shown + ": " + Shorten(e.Message));
        }
    }

    private static int NeedsApi(Assembly assembly)
    {
        try
        {
            object[] found = assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false);

            foreach (object item in found)
            {
                AssemblyMetadataAttribute meta = item as AssemblyMetadataAttribute;
                if (meta == null) continue;
                if (!string.Equals(meta.Key, DragonatorApi.RequirementKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                int needs;
                if (int.TryParse(meta.Value, out needs)) return needs;
            }
        }
        catch (Exception)
        {
        }

        return 1;
    }

    private static Type[] TypesOf(Assembly assembly, string shown)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            failures.Add(shown + ": built against a different Dragonator version");
            return e.Types;
        }
    }

    private static bool RegisterEscrow(Type[] types, string shown)
    {
        foreach (Type type in types)
        {
            if (type == null) continue;
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IMatchEscrow).IsAssignableFrom(type)) continue;

            if (escrow != null)
            {
                failures.Add(shown + ": a match escrow is already installed by " + escrowOwner +
                             " - remove one of them");
                return false;
            }

            try
            {
                IMatchEscrow found = Activator.CreateInstance(type) as IMatchEscrow;
                if (found == null) continue;

                escrow = found;
                escrowOwner = shown;
                return true;
            }
            catch (Exception e)
            {
                failures.Add(shown + " (" + type.Name + "): " + Shorten(e.Message));
            }
        }

        return false;
    }

    private static bool RegisterWitness(Type[] types, string shown)
    {
        foreach (Type type in types)
        {
            if (type == null) continue;
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IMatchWitness).IsAssignableFrom(type)) continue;

            if (witness != null)
            {
                failures.Add(shown + ": a match witness is already installed by " + witnessOwner +
                             " - remove one of them");
                return false;
            }

            try
            {
                IMatchWitness found = Activator.CreateInstance(type) as IMatchWitness;
                if (found == null) continue;

                witness = found;
                witnessOwner = shown;
                return true;
            }
            catch (Exception e)
            {
                failures.Add(shown + " (" + type.Name + "): " + Shorten(e.Message));
            }
        }

        return false;
    }

    private static bool RegisterBots(Type[] types, string shown)
    {
        foreach (Type type in types)
        {
            if (type == null) continue;
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IMatchBot).IsAssignableFrom(type)) continue;

            if (bots != null)
            {
                failures.Add(shown + ": a match bot provider is already installed by " + botsOwner +
                             " - remove one of them");
                return false;
            }

            try
            {
                IMatchBot found = Activator.CreateInstance(type) as IMatchBot;
                if (found == null) continue;

                bots = found;
                botsOwner = shown;
                return true;
            }
            catch (Exception e)
            {
                failures.Add(shown + " (" + type.Name + "): " + Shorten(e.Message));
            }
        }

        return false;
    }

    private static int RegisterWallets(Type[] types, string shown)
    {
        int added = 0;

        foreach (Type type in types)
        {
            if (type == null) continue;
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IServerWallet).IsAssignableFrom(type)) continue;

            try
            {
                IServerWallet found = Activator.CreateInstance(type) as IServerWallet;
                if (found == null) continue;

                wallets.Add(found);
                added++;
            }
            catch (Exception e)
            {
                failures.Add(shown + " (" + type.Name + "): " + Shorten(e.Message));
            }
        }

        return added;
    }

    private static int RegisterDirectories(Type[] types, string shown)
    {
        int added = 0;

        foreach (Type type in types)
        {
            if (type == null) continue;
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IServerDirectory).IsAssignableFrom(type)) continue;

            try
            {
                IServerDirectory found = Activator.CreateInstance(type) as IServerDirectory;
                if (found == null) continue;

                ServerDirectory.Register(found);
                added++;
            }
            catch (Exception e)
            {
                failures.Add(shown + " (" + type.Name + "): " + Shorten(e.Message));
            }
        }

        return added;
    }

    private static int RegisterOptions(Type[] types, string shown)
    {
        List<IServerOption> found = new List<IServerOption>();

        foreach (Type type in types)
        {
            if (type == null) continue;
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IServerOption).IsAssignableFrom(type)) continue;

            try
            {
                IServerOption option = Activator.CreateInstance(type) as IServerOption;
                if (option == null) continue;

                found.Add(option);
            }
            catch (Exception e)
            {
                failures.Add(shown + " (" + type.Name + "): " + Shorten(e.Message));
            }
        }

        Sort(found);

        foreach (IServerOption option in found) ServerOptions.Register(option);

        return found.Count;
    }

    private static void Sort(List<IServerOption> options)
    {
        int[] keys = new int[options.Count];
        for (int i = 0; i < options.Count; i++) keys[i] = ServerOptions.OrderOf(options[i]);

        for (int i = 1; i < options.Count; i++)
        {
            int key = keys[i];
            IServerOption option = options[i];
            int j = i - 1;

            while (j >= 0 && keys[j] > key)
            {
                keys[j + 1] = keys[j];
                options[j + 1] = options[j];
                j--;
            }

            keys[j + 1] = key;
            options[j + 1] = option;
        }
    }

    private static string Describe(Assembly assembly)
    {
        try
        {
            AssemblyName name = assembly.GetName();
            Version version = name.Version;

            return version == null
                ? name.Name
                : name.Name + " " + version.Major + "." + version.Minor + "." + version.Build;
        }
        catch (Exception)
        {
            return Path.GetFileName(assembly.Location);
        }
    }

    private static void WriteReadme(string folder)
    {
        try
        {
            string path = Path.Combine(folder, ReadmeFile);
            if (File.Exists(path)) return;

            File.WriteAllLines(path, ReadmeTemplate);
        }
        catch (Exception)
        {
        }
    }

    private static string Plural(int count, string word)
    {
        return count + " " + word;
    }

    private static string Shorten(string text)
    {
        if (string.IsNullOrEmpty(text)) return "no detail";

        string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 60 ? flat : flat.Substring(0, 57) + "...";
    }

    private static bool Enabled()
    {
        return HasFlag(ForceFlag) || Utils.IsHeadless();
    }

    private static bool HasFlag(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
