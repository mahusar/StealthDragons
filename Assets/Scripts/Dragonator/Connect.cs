using System;
using System.IO;
using System.Net.Sockets;
using System.Collections;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Socks5;
using Mirror;

public class Connect : MonoBehaviour
{
    [SerializeField] private TMP_InputField onionInputField;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text playersText;
    private const string gameVersion = "0.6";  // GAME VERSION
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private TMP_Text versionNumberText;
    [SerializeField] private TMP_Text serverInfoText;

    // Stores the actual port returned by the server via GET_ROOMS
    private int _lastKnownServerPort = 7780;

    private void Awake()
    {
        connectButton.onClick.AddListener(OnConnectClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        joinButton.gameObject.SetActive(false);

        string saved = TorConfig.GetSavedOnionAddress();
        if (!string.IsNullOrEmpty(saved))
            onionInputField.text = saved;
    }
    private void Start()
    {
        if (versionNumberText != null)
            versionNumberText.text = $"{gameVersion}";
    }

    private void OnConnectClicked()
    {
        string address = onionInputField.text.Trim();
        if (string.IsNullOrEmpty(address))
        {
            statusText.text = "Enter a .onion address first.";
            return;
        }

        TorConfig.SaveOnionAddress(address);
        StartCoroutine(PingAndStatus(address));
    }

    private IEnumerator PingAndStatus(string address)
    {
        statusText.text = "Pinging server...";
        playersText.text = "";
        if (versionText != null) versionText.text = "";
        if (serverInfoText != null) serverInfoText.text = "";
        connectButton.interactable = false;
        joinButton.gameObject.SetActive(false);

        // ── Step 1: ping ──────────────────────────────────────────────────────
        bool serverOnline = false;
        long rttMs = 0;
        bool done = false;

        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var tcp = new TcpClient();
                tcp.ConnectThroughProxyAsync(TorConfig.SocksHost, TorConfig.SocksPort, address, TorConfig.MatchmakerPort)
                    .GetAwaiter().GetResult();
                sw.Stop();
                rttMs = sw.ElapsedMilliseconds;
                serverOnline = tcp.Connected;
                tcp.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connect] Ping failed: {e.Message}");
                serverOnline = false;
            }
            finally { done = true; }
        });
        thread.IsBackground = true;
        thread.Start();
        while (!done) yield return null;

        if (!serverOnline)
        {
            statusText.text = "Server unreachable";
            connectButton.interactable = true;
            yield break;
        }

        statusText.text = $"Server online: {rttMs}ms";

        // ── Step 2: GET_STATUS — player count ────────────────────────────────
        string playerCount = "?";
        done = false;

        var thread2 = new System.Threading.Thread(() =>
        {
            try
            {
                var tcp = new TcpClient();
                tcp.ConnectThroughProxyAsync(TorConfig.SocksHost, TorConfig.SocksPort, address, TorConfig.MatchmakerPort)
                    .GetAwaiter().GetResult();

                using (tcp)
                using (var stream = tcp.GetStream())
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    writer.WriteLine("GET_STATUS");
                    playerCount = reader.ReadLine() ?? "?";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connect] Status fetch failed: {e.Message}");
                playerCount = "?";
            }
            finally { done = true; }
        });
        thread2.IsBackground = true;
        thread2.Start();
        while (!done) yield return null;

        playersText.text = $"Players waiting: {playerCount}/2";

        // ── Step 3: GET_VERSION ───────────────────────────────────────────────
        string serverVersion = "";
        done = false;

        var thread3 = new System.Threading.Thread(() =>
        {
            try
            {
                var tcp = new TcpClient();
                tcp.ConnectThroughProxyAsync(TorConfig.SocksHost, TorConfig.SocksPort, address, TorConfig.MatchmakerPort)
                    .GetAwaiter().GetResult();

                using (tcp)
                using (var stream = tcp.GetStream())
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    writer.WriteLine("GET_VERSION");
                    serverVersion = reader.ReadLine() ?? "";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connect] Version check failed: {e.Message}");
                serverVersion = "";
            }
            finally { done = true; }
        });
        thread3.IsBackground = true;
        thread3.Start();
        while (!done) yield return null;

        if (serverVersion != gameVersion)
        {
            if (versionText != null)
            {
                versionText.text = $"Version mismatch! Client: {gameVersion} Server: {serverVersion}";
                versionText.color = Color.red;
            }
            connectButton.interactable = true;
            yield break; // block join
        }

        if (versionText != null)
        {
            versionText.text = $"Version: OK";
        }

        // ── Step 4: GET_SERVERINFO — how this server is configured ───────────
        string serverInfo = "";
        done = false;

        var thread4 = new System.Threading.Thread(() =>
        {
            try
            {
                var tcp = new TcpClient();
                tcp.ConnectThroughProxyAsync(TorConfig.SocksHost, TorConfig.SocksPort, address, TorConfig.MatchmakerPort)
                    .GetAwaiter().GetResult();

                using (tcp)
                using (var stream = tcp.GetStream())
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    writer.WriteLine("GET_SERVERINFO");
                    serverInfo = reader.ReadLine() ?? "";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connect] Server info fetch failed: {e.Message}");
                serverInfo = "";
            }
            finally { done = true; }
        });
        thread4.IsBackground = true;
        thread4.Start();
        while (!done) yield return null;

        ShowServerInfo(serverInfo);

        joinButton.gameObject.SetActive(true);
        connectButton.interactable = true;
    }

    private void ShowServerInfo(string wire)
    {
        if (serverInfoText == null) return;

        if (string.IsNullOrEmpty(wire))
        {
            serverInfoText.text = "Server settings unavailable";
            return;
        }

        StringBuilder lines = new StringBuilder();
        decimal bet = -1m;
        decimal fee = -1m;

        foreach (string pair in wire.Split(';'))
        {
            int split = pair.IndexOf('=');
            if (split <= 0) continue;

            string key = pair.Substring(0, split).Trim();
            string value = pair.Substring(split + 1).Trim();

            decimal number;
            bool isNumber = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number);
            if (isNumber && key == "bet") bet = number;
            if (isNumber && key == "fee") fee = number;

            if (lines.Length > 0) lines.Append('\n');
            lines.Append(DescribeSetting(key, value));
        }

        if (bet > 0m && fee >= 0m)
        {
            decimal payout = bet * 2m - fee;
            if (payout > 0m)
                lines.Append($"\nWinner receives: {Format(payout)} XST");
        }

        serverInfoText.text = lines.Length > 0 ? lines.ToString() : "Server settings unavailable";
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private string DescribeSetting(string key, string value)
    {
        if (key == "bet") return $"Bet: {value} XST per player";

        if (key == "fee")
        {
            decimal fee;
            bool parsed = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out fee);
            if (parsed && fee <= 0m) return "Host fee: none";
            return $"Host fee: {value} XST per match";
        }

        return $"{key}: {value}";
    }

    private void OnJoinClicked()
    {
        string address = TorConfig.GetSavedOnionAddress();
        if (string.IsNullOrEmpty(address)) return;

        joinButton.interactable = false;
        connectButton.interactable = false;

        var manager = NetworkManager.singleton as XSTDragonNetworkManager;
        if (manager != null)
        {
            manager.networkAddress = address;
            manager.networkPort = 7780; // hardcoded, no loop
            manager.UpdateTransportPort();
            Debug.Log($"[Connect] Joining {address}:7780");
            manager.StartClient();
        }
    }
    public void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}