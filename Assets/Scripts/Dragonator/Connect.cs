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
    [SerializeField] private Button swapButton;
    [SerializeField] private GameObject swapPanel;
    [SerializeField] private TMP_Text swapDetailsText;
    [SerializeField] private GameObject playerButton;
    [SerializeField] private GameObject practiceButton;
    [SerializeField] private GameObject settingsButton;
    [SerializeField] private GameObject exitButton;

    // Stores the actual port returned by the server via GET_ROOMS
    private int _lastKnownServerPort = 7780;

    private const float DisabledLabelAlpha = 0.35f;

    private TMP_Text joinLabel;
    private Color joinLabelColour = Color.white;

    private bool menuHidden;
    private bool playerVisibleBeforeSwap;
    private bool practiceVisibleBeforeSwap;
    private bool settingsVisibleBeforeSwap;
    private bool exitVisibleBeforeSwap;

    private bool swapSupported;
    private string swapAsset = "XMR";
    private string swapRate = "";
    private string swapMinimum = "";
    private string swapConfirmations = "";

    private void Awake()
    {
        connectButton.onClick.AddListener(OnConnectClicked);
        joinButton.onClick.AddListener(OnJoinClicked);

        joinLabel = joinButton.GetComponentInChildren<TMP_Text>();
        if (joinLabel != null) joinLabelColour = joinLabel.color;

        joinButton.gameObject.SetActive(true);
        SetJoinEnabled(false);

        if (swapButton != null) swapButton.onClick.AddListener(OnSwapClicked);
        ShowSwapOffer(false);
        CloseSwapPanel();

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
        CloseSwapPanel();
        connectButton.interactable = false;
        SetJoinEnabled(false);
        ShowSwapOffer(false);

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

        SetJoinEnabled(true);
        ShowSwapOffer(swapSupported);
        connectButton.interactable = true;
    }

    private void SetJoinEnabled(bool enabled)
    {
        joinButton.interactable = enabled;

        if (joinLabel == null) return;

        joinLabel.color = enabled
            ? joinLabelColour
            : new Color(joinLabelColour.r, joinLabelColour.g, joinLabelColour.b,
                        joinLabelColour.a * DisabledLabelAlpha);
    }

    private void ShowServerInfo(string wire)
    {
        swapSupported = false;
        swapRate = "";
        swapMinimum = "";
        swapConfirmations = "";

        if (string.IsNullOrEmpty(wire))
        {
            if (serverInfoText != null) serverInfoText.text = "Server settings unavailable";
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

            if (key == "swap")
            {
                ReadSwapOffer(value);
                continue;
            }

            if (key == "swapmin")
            {
                swapMinimum = value;
                continue;
            }

            if (key == "swapconf")
            {
                swapConfirmations = value;
                continue;
            }

            if (lines.Length > 0) lines.Append('\n');
            lines.Append(DescribeSetting(key, value));
        }

        if (bet > 0m && fee >= 0m)
        {
            decimal payout = bet * 2m - fee;
            if (payout > 0m)
                lines.Append($"\nWinner receives: {Format(payout)} XST");
        }

        if (swapSupported)
        {
            if (lines.Length > 0) lines.Append('\n');
            lines.Append($"Swap: {swapRate} XST per {swapAsset}");
        }

        if (serverInfoText != null)
            serverInfoText.text = lines.Length > 0 ? lines.ToString() : "Server settings unavailable";
    }

    private void ReadSwapOffer(string value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return;

        int at = value.IndexOf('@');
        if (at > 0)
        {
            swapAsset = value.Substring(0, at).Trim().ToUpperInvariant();
            swapRate = value.Substring(at + 1).Trim();
        }
        else
        {
            swapRate = value;
        }

        swapSupported = swapRate.Length > 0;
    }

    private void ShowSwapOffer(bool offered)
    {
        if (swapButton == null) return;

        swapButton.gameObject.SetActive(offered);
    }

    private void OnSwapClicked()
    {
        if (swapPanel == null) return;

        if (swapPanel.activeSelf)
        {
            CloseSwapPanel();
            return;
        }

        if (swapDetailsText != null) swapDetailsText.text = DescribeSwapOffer();
        swapPanel.SetActive(true);
        HideMenuForSwap(true);
    }

    private void CloseSwapPanel()
    {
        if (swapPanel != null) swapPanel.SetActive(false);
        HideMenuForSwap(false);
    }

    private void HideMenuForSwap(bool hidden)
    {
        if (hidden == menuHidden) return;
        menuHidden = hidden;

        if (hidden)
        {
            playerVisibleBeforeSwap = playerButton == null || playerButton.activeSelf;
            practiceVisibleBeforeSwap = practiceButton == null || practiceButton.activeSelf;
            settingsVisibleBeforeSwap = settingsButton == null || settingsButton.activeSelf;
            exitVisibleBeforeSwap = exitButton == null || exitButton.activeSelf;

            if (playerButton != null) playerButton.SetActive(false);
            if (practiceButton != null) practiceButton.SetActive(false);
            if (settingsButton != null) settingsButton.SetActive(false);
            if (exitButton != null) exitButton.SetActive(false);

            return;
        }

        if (playerButton != null) playerButton.SetActive(playerVisibleBeforeSwap);
        if (practiceButton != null) practiceButton.SetActive(practiceVisibleBeforeSwap);
        if (settingsButton != null) settingsButton.SetActive(settingsVisibleBeforeSwap);
        if (exitButton != null) exitButton.SetActive(exitVisibleBeforeSwap);
    }

    private string DescribeSwapOffer()
    {
        if (!swapSupported) return "This server does not offer swapping.";

        StringBuilder sb = new StringBuilder();
        sb.Append($"Swap {swapAsset} for XST\n\n");
        sb.Append($"Rate: {swapRate} XST per {swapAsset}");

        if (swapMinimum.Length > 0) sb.Append($"\nMinimum: {swapMinimum} {swapAsset}");
        if (swapConfirmations.Length > 0) sb.Append($"\nConfirmations required: {swapConfirmations}");

        sb.Append("\n\nDeposits are not enabled on this client yet.");
        return sb.ToString();
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

        SetJoinEnabled(false);
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