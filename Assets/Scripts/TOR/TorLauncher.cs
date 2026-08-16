using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Mirror;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class TorLauncher
{
    public enum State
    {
        Idle,
        Starting,
        Ready,
        Failed
    }

    private const int FirstPort = 9250;
    private const int LastPort = 9299;
    private const int ProbeTimeoutMs = 400;
    private const string Folder = "tor";

    private static readonly object gate = new object();

    private static Process process;
    private static State state = State.Idle;
    private static int percent;
    private static string message = "";
    private static bool usingExisting;
    private static int socksPort;

    public static State Status
    {
        get { lock (gate) return state; }
    }

    public static int Percent
    {
        get { lock (gate) return percent; }
    }

    public static string Message
    {
        get { lock (gate) return message; }
    }

    public static bool UsingExisting
    {
        get { lock (gate) return usingExisting; }
    }

    public static bool Ready
    {
        get { return Status == State.Ready; }
    }

    public static string Describe()
    {
        lock (gate)
        {
            switch (state)
            {
                case State.Idle: return "Tor has not been started yet.";
                case State.Starting: return percent > 0
                    ? "Starting Tor... " + percent + "%"
                    : "Starting Tor...";
                case State.Ready: return usingExisting
                    ? "Using the Tor already running on this machine."
                    : "Tor is ready.";
                default: return string.IsNullOrEmpty(message) ? "Tor could not start." : message;
            }
        }
    }

    public static void Ensure()
    {
        lock (gate)
        {
            if (state == State.Starting || state == State.Ready) return;
            state = State.Starting;
            percent = 0;
            message = "";
        }

        if (Utils.IsHeadless())
        {
            Settle(State.Ready, "A dedicated server uses the system Tor.", true, TorConfig.DefaultSocksPort);
            return;
        }

        if (Listening(TorConfig.DefaultSocksHost, TorConfig.DefaultSocksPort))
        {
            Debug.Log("[Tor] found an existing Tor on " + TorConfig.DefaultSocksPort + ", using it.");
            Settle(State.Ready, "", true, TorConfig.DefaultSocksPort);
            return;
        }

        string binary = BundledBinary();
        if (string.IsNullOrEmpty(binary))
        {
            Settle(State.Failed,
                "Tor is not running and no bundled copy was found. Start Tor yourself, then press CONNECT again.",
                false, 0);
            return;
        }

        try
        {
            Launch(binary);
        }
        catch (Exception e)
        {
            Settle(State.Failed, "Tor could not start (" + e.GetType().Name + "). Start Tor yourself and try again.",
                false, 0);
        }
    }

    public static void Stop()
    {
        Process running;

        lock (gate)
        {
            running = process;
            process = null;
            if (state != State.Failed) state = State.Idle;
            percent = 0;
        }

        if (running == null) return;

        try
        {
            if (!running.HasExited)
            {
                running.Kill();
                running.WaitForExit(3000);
            }
        }
        catch (Exception)
        {
        }

        try
        {
            running.Dispose();
        }
        catch (Exception)
        {
        }

        Debug.Log("[Tor] stopped the bundled Tor.");
    }

    private static void Launch(string binary)
    {
        int port = FreePort();
        if (port == 0)
        {
            Settle(State.Failed, "No free port for Tor between " + FirstPort + " and " + LastPort + ".", false, 0);
            return;
        }

        string root = Path.Combine(Application.persistentDataPath, Folder);
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);

        string torrc = Path.Combine(root, "torrc");
        File.WriteAllText(torrc, Torrc(port, data), new UTF8Encoding(false));

        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = "-f \"" + torrc + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(binary)
        };

        Process started = new Process { StartInfo = info, EnableRaisingEvents = true };

        started.OutputDataReceived += (sender, args) => ReadLine(args.Data, port);
        started.ErrorDataReceived += (sender, args) => ReadLine(args.Data, port);
        started.Exited += (sender, args) => Exited();

        started.Start();
        started.BeginOutputReadLine();
        started.BeginErrorReadLine();

        lock (gate) process = started;

        Debug.Log("[Tor] started the bundled Tor on SOCKS port " + port + ".");
    }

    private static string Torrc(int port, string data)
    {
        StringBuilder sb = new StringBuilder();

        sb.Append("SocksPort ").Append(port).Append('\n');
        sb.Append("DataDirectory \"").Append(TorrcPath(data)).Append("\"\n");
        sb.Append("ClientOnly 1\n");
        sb.Append("Log notice stdout\n");
        sb.Append("AvoidDiskWrites 1\n");

        return sb.ToString();
    }

    private static string TorrcPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        string native = Path.DirectorySeparatorChar == '\\'
            ? path.Replace('/', '\\')
            : path.Replace('\\', '/');

        return native.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static void ReadLine(string line, int port)
    {
        if (string.IsNullOrEmpty(line)) return;

        int at = line.IndexOf("Bootstrapped ", StringComparison.Ordinal);
        if (at < 0) return;

        int start = at + "Bootstrapped ".Length;
        int end = start;
        while (end < line.Length && char.IsDigit(line[end])) end++;

        int found;
        if (end == start || !int.TryParse(line.Substring(start, end - start), out found)) return;

        lock (gate)
        {
            if (found > percent) percent = found;
        }

        if (found >= 100) Settle(State.Ready, "", false, port);
    }

    private static void Exited()
    {
        lock (gate)
        {
            if (state == State.Ready) return;
            state = State.Failed;
            if (string.IsNullOrEmpty(message))
                message = "Tor stopped before it finished starting.";
        }
    }

    private static void Settle(State next, string text, bool existing, int port)
    {
        lock (gate)
        {
            state = next;
            message = text;
            usingExisting = existing;
            socksPort = port;
            if (next == State.Ready) percent = 100;
        }

        if (next == State.Ready && port > 0)
            TorConfig.SetSocksProxy(TorConfig.DefaultSocksHost, port);
    }

    private static string BundledBinary()
    {
        string name = Application.platform == RuntimePlatform.WindowsPlayer ||
                      Application.platform == RuntimePlatform.WindowsEditor
            ? "tor.exe"
            : "tor";

        try
        {
            string direct = Path.Combine(Application.streamingAssetsPath, Path.Combine("Tor", name));
            if (File.Exists(direct)) return direct;

            string root = Path.Combine(Application.streamingAssetsPath, "Tor");
            if (!Directory.Exists(root)) return null;

            string[] found = Directory.GetFiles(root, name, SearchOption.AllDirectories);
            return found.Length > 0 ? found[0] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int FreePort()
    {
        for (int port = FirstPort; port <= LastPort; port++)
            if (!Listening(TorConfig.DefaultSocksHost, port)) return port;

        return 0;
    }

    private static bool Listening(string host, int port)
    {
        try
        {
            using (TcpClient probe = new TcpClient())
            {
                IAsyncResult pending = probe.BeginConnect(host, port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(ProbeTimeoutMs)) return false;

                probe.EndConnect(pending);
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }
}
