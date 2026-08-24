using System;
using System.IO;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
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
    private const string gameVersion = "0.7";  // GAME VERSION
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private TMP_Text versionNumberText;
    [SerializeField] private TMP_Text serverInfoText;
    [SerializeField] private TMP_Text serverDetailText;
    [SerializeField] private GameObject addonButtons;
    [SerializeField] private Button swapButton;
    [SerializeField] private GameObject swapPanel;
    [SerializeField] private TMP_Text swapDetailsText;
    [SerializeField] private TMP_InputField swapAddressInput;
    [SerializeField] private Button swapSubmitButton;
    [SerializeField] private TMP_Text swapResultText;
    [SerializeField] private Button serversButton;
    [SerializeField] private GameObject serversPanel;
    [SerializeField] private TMP_Text serversText;
    [SerializeField] private Button serversCloseButton;

    // Stores the actual port returned by the server via GET_ROOMS
    private int _lastKnownServerPort = 7780;

    private const float DisabledLabelAlpha = 0.35f;

    private TMP_Text joinLabel;
    private Color joinLabelColour = Color.white;

    private string detailAddress = "";
    private string detailStatus = "";
    private string detailVersion = "";
    private string detailPlayers = "";
    private string detailSettings = "";
    private string detailAddons = "";

    private bool swapSupported;
    private string swapAsset = "XMR";
    private string swapRate = "";
    private string swapMinimum = "";
    private string swapConfirmations = "";
    private int swapPort;
    private bool swapRequestRunning;

    private const int MaxDiscovered = 24;
    private const int ProbeWorkers = 6;
    private const int ProbeTimeoutMs = 20000;
    private const float DiscoveryTimeout = 60f;

    private bool discoveryRunning;
    private readonly List<Discovered> discovered = new List<Discovered>();

    private class Discovered
    {
        public string Onion;
        public int Port;
        public volatile bool Done;
        public volatile bool Online;
        public string Info;

        public string Entry
        {
            get { return Onion + ":" + Port.ToString(CultureInfo.InvariantCulture); }
        }
    }

    private void Awake()
    {
        connectButton.onClick.AddListener(OnConnectClicked);
        joinButton.onClick.AddListener(OnJoinClicked);

        joinLabel = joinButton.GetComponentInChildren<TMP_Text>();
        if (joinLabel != null) joinLabelColour = joinLabel.color;

        joinButton.gameObject.SetActive(true);
        SetJoinEnabled(false);

        if (swapButton != null) swapButton.onClick.AddListener(OnSwapClicked);
        if (swapSubmitButton != null) swapSubmitButton.onClick.AddListener(OnSwapSubmitClicked);
        ShowSwapOffer(false);
        CloseSwapPanel();

        if (serversButton != null) serversButton.onClick.AddListener(OnServersClicked);
        if (serversCloseButton != null) serversCloseButton.onClick.AddListener(CloseServersPanel);
        CloseServersPanel();

        ShowAddonButtons(false);
        RenderServerDetail();

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

    private const float TorStartTimeout = 120f;

    private IEnumerator WaitForTor()
    {
        if (TorLauncher.Ready) yield break;

        TorLauncher.Ensure();

        float deadline = Time.realtimeSinceStartup + TorStartTimeout;

        while (Time.realtimeSinceStartup < deadline)
        {
            TorLauncher.State status = TorLauncher.Status;
            if (status == TorLauncher.State.Ready || status == TorLauncher.State.Failed) yield break;

            statusText.text = TorLauncher.Describe();
            yield return new WaitForSeconds(0.25f);
        }

        Debug.LogWarning("[Tor] gave up waiting after " + TorStartTimeout + "s.");
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
        ShowAddonButtons(false);

        detailAddress = address;
        detailStatus = "asking...";
        detailVersion = "";
        detailPlayers = "";
        detailSettings = "";
        detailAddons = "";
        RenderServerDetail();

        yield return WaitForTor();

        if (!TorLauncher.Ready)
        {
            statusText.text = TorLauncher.Describe();
            detailStatus = "Tor is not ready";
            RenderServerDetail();
            connectButton.interactable = true;
            yield break;
        }

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
            detailStatus = "unreachable";
            RenderServerDetail();
            connectButton.interactable = true;
            yield break;
        }

        statusText.text = $"Server online: {rttMs}ms";
        detailStatus = $"online, {rttMs}ms away";
        RenderServerDetail();

        // ── Step 2: GET_STATUS - player count ────────────────────────────────
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
        detailPlayers = $"{playerCount}/2 waiting to play";
        RenderServerDetail();

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

            detailVersion = serverVersion.Length == 0
                ? $"version unknown, this client is {gameVersion}"
                : $"version {serverVersion}, this client is {gameVersion} - cannot join";
            RenderServerDetail();

            connectButton.interactable = true;
            yield break; // block join
        }

        if (versionText != null)
        {
            versionText.text = $"Version: OK";
        }

        detailVersion = $"version {serverVersion}";
        RenderServerDetail();

        // ── Step 4: GET_SERVERINFO - how this server is configured ───────────
        string serverInfo = null;
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
                    serverInfo = reader.ReadLine();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connect] Server info fetch failed: {e.Message}");
                serverInfo = null;
            }
            finally { done = true; }
        });
        thread4.IsBackground = true;
        thread4.Start();
        while (!done) yield return null;

        ShowServerInfo(serverInfo);

        SetJoinEnabled(true);
        ShowSwapOffer(swapSupported);
        ShowAddonButtons(true);
        connectButton.interactable = true;
    }

    private void ShowAddonButtons(bool connected)
    {
        if (serversButton != null) serversButton.gameObject.SetActive(connected);
        if (addonButtons != null) addonButtons.SetActive(connected);
    }

    private void RenderServerDetail()
    {
        if (serverDetailText == null) return;

        if (detailAddress.Length == 0)
        {
            serverDetailText.text = "Not connected.\n\nEnter a .onion address and press CONNECT to see what that server offers.";
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append(ShortOnion(detailAddress));

        if (detailStatus.Length > 0) sb.Append('\n').Append(detailStatus);
        if (detailVersion.Length > 0) sb.Append('\n').Append(detailVersion);
        if (detailPlayers.Length > 0) sb.Append('\n').Append(detailPlayers);

        if (detailSettings.Length > 0) sb.Append("\n\n").Append(detailSettings);

        if (detailAddons.Length > 0) sb.Append("\n\nAdd-ons\n").Append(detailAddons);

        serverDetailText.text = sb.ToString();
    }

    private static string DescribeAddons(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        StringBuilder sb = new StringBuilder();

        foreach (string name in value.Split(','))
        {
            string trimmed = name.Trim();
            if (trimmed.Length == 0) continue;

            if (sb.Length > 0) sb.Append('\n');
            sb.Append(trimmed);
        }

        return sb.ToString();
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
        swapPort = 0;

        if (wire == null)
        {
            if (serverInfoText != null) serverInfoText.text = "Server settings unavailable";
            detailSettings = "Settings unavailable";
            detailAddons = "";
            RenderServerDetail();
            return;
        }

        if (wire.Trim().Length == 0)
        {
            if (serverInfoText != null) serverInfoText.text = "Free match - no bet, no fee";
            detailSettings = "Free match - no bet, no fee";
            detailAddons = "none installed";
            RenderServerDetail();
            return;
        }

        StringBuilder lines = new StringBuilder();
        decimal bet = -1m;
        decimal fee = -1m;
        string addons = "";

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

            if (key == "swapport")
            {
                int port;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) &&
                    port > 0 && port <= 65535)
                    swapPort = port;
                continue;
            }

            if (key == "addons")
            {
                addons = value;
                continue;
            }

            if (lines.Length > 0) lines.Append('\n');
            lines.Append(DescribeSetting(key, value));
        }

        swapSupported = swapSupported && swapPort > 0;

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

        detailSettings = lines.Length > 0 ? lines.ToString() : "Free match - no bet, no fee";
        detailAddons = addons.Length > 0 ? DescribeAddons(addons) : "none installed";
        RenderServerDetail();
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
        if (swapResultText != null) swapResultText.text = "";
        if (swapAddressInput != null) swapAddressInput.text = "";
        SetSwapFormEnabled(true);

        swapPanel.SetActive(true);
    }

    private void CloseSwapPanel()
    {
        if (swapPanel != null) swapPanel.SetActive(false);
    }

    private string DescribeSwapOffer()
    {
        if (!swapSupported) return "This server does not offer swapping.";

        StringBuilder sb = new StringBuilder();
        sb.Append($"Swap {swapAsset} for XST\n");
        sb.Append($"Rate: {swapRate} XST per {swapAsset}");

        if (swapMinimum.Length > 0) sb.Append($"   Minimum: {swapMinimum} {swapAsset}");
        if (swapConfirmations.Length > 0) sb.Append($"   Confirmations: {swapConfirmations}");

        sb.Append("\n\nEnter an XST address you own. The rate is locked when the deposit address is issued.");
        return sb.ToString();
    }

    private void SetSwapFormEnabled(bool enabled)
    {
        if (swapAddressInput != null) swapAddressInput.interactable = enabled;
        if (swapSubmitButton != null) swapSubmitButton.interactable = enabled;
    }

    private void OnSwapSubmitClicked()
    {
        if (swapRequestRunning) return;

        if (!swapSupported || swapPort <= 0)
        {
            if (swapResultText != null) swapResultText.text = "This server is not offering swaps right now.";
            return;
        }

        string payoutAddress = swapAddressInput == null ? "" : swapAddressInput.text.Trim();
        if (payoutAddress.Length == 0)
        {
            if (swapResultText != null) swapResultText.text = "Enter the XST address you want the coins sent to.";
            return;
        }

        string onion = TorConfig.GetSavedOnionAddress();
        if (string.IsNullOrEmpty(onion))
        {
            if (swapResultText != null) swapResultText.text = "Connect to a server first.";
            return;
        }

        StartCoroutine(RequestDepositAddress(onion, payoutAddress));
    }

    private IEnumerator RequestDepositAddress(string onion, string payoutAddress)
    {
        swapRequestRunning = true;
        SetSwapFormEnabled(false);
        if (swapResultText != null) swapResultText.text = "Asking the server for a deposit address...";

        string reply = "";
        bool done = false;
        int port = swapPort;

        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var tcp = new TcpClient();
                tcp.ConnectThroughProxyAsync(TorConfig.SocksHost, TorConfig.SocksPort, onion, port)
                    .GetAwaiter().GetResult();

                using (tcp)
                using (var stream = tcp.GetStream())
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    writer.WriteLine($"SWAP_NEW|{payoutAddress}");
                    reply = reader.ReadLine() ?? "";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connect] Swap request failed: {e.Message}");
                reply = "";
            }
            finally { done = true; }
        });
        thread.IsBackground = true;
        thread.Start();
        while (!done) yield return null;

        if (swapResultText != null) swapResultText.text = DescribeSwapReply(reply, payoutAddress);

        SetSwapFormEnabled(true);
        swapRequestRunning = false;
    }

    private string DescribeSwapReply(string reply, string payoutAddress)
    {
        if (string.IsNullOrEmpty(reply))
            return "The swap desk did not answer. Try again in a moment.";

        string[] parts = reply.Split('|');

        if (parts[0] == "ERROR")
            return parts.Length > 1 ? $"Refused: {parts[1]}" : "The swap desk refused the request.";

        if (parts[0] != "OK" || parts.Length < 2)
            return "The swap desk sent something this client did not understand.";

        string deposit = parts[1];
        string rate = parts.Length > 2 ? parts[2] : swapRate;
        string minimum = parts.Length > 3 ? parts[3] : swapMinimum;
        string confirmations = parts.Length > 4 ? parts[4] : swapConfirmations;

        StringBuilder sb = new StringBuilder();
        sb.Append($"Send {swapAsset} to this address, yours alone:\n{deposit}\n\n");
        sb.Append($"Locked at {rate} XST per {swapAsset}");

        if (minimum.Length > 0) sb.Append($"   Send at least {minimum} {swapAsset}");
        if (confirmations.Length > 0) sb.Append($"\nXST arrives after {confirmations} confirmations");

        sb.Append($"\nXST goes to {payoutAddress}");
        return sb.ToString();
    }

    private void OnServersClicked()
    {
        if (serversPanel == null) return;

        if (serversPanel.activeSelf)
        {
            CloseServersPanel();
            return;
        }

        serversPanel.SetActive(true);

        if (discoveryRunning) return;

        string address = onionInputField == null ? "" : onionInputField.text.Trim();
        if (address.Length == 0) address = TorConfig.GetSavedOnionAddress();

        if (string.IsNullOrEmpty(address))
        {
            SetServersText("Enter a .onion address first, then ask that server who else it knows.");
            return;
        }

        StartCoroutine(DiscoverServers(address));
    }

    private void CloseServersPanel()
    {
        if (serversPanel != null) serversPanel.SetActive(false);
    }

    private void SetServersText(string text)
    {
        if (serversText != null) serversText.text = text;
    }

    private string savedServersText;
    private bool serversPanelWasOpen;

    public TMP_Text OpenSideList(string title)
    {
        if (savedServersText == null)
        {
            savedServersText = serversText != null ? serversText.text : "";
            serversPanelWasOpen = serversPanel != null && serversPanel.activeSelf;
            savedHeadline = Headline() != null ? Headline().text : "";
        }

        if (serversPanel != null) serversPanel.SetActive(true);
        if (Headline() != null) Headline().text = title;

        Silence();

        if (serversText != null) serversText.raycastTarget = true;

        AddScrollbar();

        return serversText;
    }

    private void Silence()
    {
        if (serversText == null) return;

        ReplayPick replay = serversText.GetComponent<ReplayPick>();

        if (replay != null)
        {
            replay.picked = null;
            replay.hovered = null;
            replay.enabled = false;
        }

        DeckPick deck = serversText.GetComponent<DeckPick>();

        if (deck != null)
        {
            deck.added = null;
            deck.removed = null;
            deck.hovered = null;
            deck.enabled = false;
        }
    }

    public DeckPick OpenDeckList()
    {
        TMP_Text where = OpenSideList("your deck");
        if (where == null) return null;

        DeckPick pick = where.GetComponent<DeckPick>();
        if (pick == null) pick = where.gameObject.AddComponent<DeckPick>();

        pick.enabled = true;

        return pick;
    }

    public void SetSideText(string text)
    {
        SetServersText(text);
    }

    public void ShowReplayList()
    {
        OpenSideList("matches");

        MakeListUsable();

        listed = ReplayList.Newest();
        SetServersText(ReplayList.Describe(listed, 0));
    }

    public void CloseSideList()
    {
        HideReplayList();
    }

    public void HideReplayList()
    {
        if (savedServersText == null) return;

        Silence();

        SetServersText(savedServersText);

        if (Headline() != null) Headline().text = savedHeadline;
        if (serversPanel != null) serversPanel.SetActive(serversPanelWasOpen);

        savedServersText = null;
    }

    private System.Collections.Generic.List<ReplayList.Entry> listed;

    private void Relight(int row)
    {
        if (listed == null || savedServersText == null) return;

        SetServersText(ReplayList.Describe(listed, row));
    }

    private string savedHeadline = "";
    private TMP_Text headline;

    private TMP_Text Headline()
    {
        if (headline != null) return headline;
        if (serversPanel == null) return null;

        foreach (TMP_Text found in serversPanel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (found.transform.parent == null) continue;
            if (!found.transform.parent.name.Contains("Header")) continue;

            headline = found;
            break;
        }

        return headline;
    }

    private void MakeListUsable()
    {
        if (serversText == null) return;

        serversText.raycastTarget = true;

        ReplayPick pick = serversText.GetComponent<ReplayPick>();
        if (pick == null) pick = serversText.gameObject.AddComponent<ReplayPick>();

        pick.enabled = true;

        ReplayUI viewer = GetComponent<ReplayUI>();
        pick.picked = viewer != null ? new System.Action<int>(viewer.PickFromList) : null;
        pick.hovered = Relight;

        AddScrollbar();
    }

    private void AddScrollbar()
    {
        ScrollRect scroll = serversPanel != null ? serversPanel.GetComponentInChildren<ScrollRect>(true) : null;
        if (scroll == null || scroll.verticalScrollbar != null) return;

        GameObject bar = new GameObject("ServersScrollbar", typeof(RectTransform));
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.SetParent(scroll.transform, false);
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = Vector2.one;
        barRect.pivot = new Vector2(1f, 1f);
        barRect.offsetMin = new Vector2(-10f, 0f);
        barRect.offsetMax = Vector2.zero;

        Image track = bar.AddComponent<Image>();
        track.color = new Color(1f, 1f, 1f, 0.08f);

        GameObject area = new GameObject("Sliding Area", typeof(RectTransform));
        RectTransform areaRect = area.GetComponent<RectTransform>();
        areaRect.SetParent(barRect, false);
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = Vector2.zero;
        areaRect.offsetMax = Vector2.zero;

        GameObject grip = new GameObject("Handle", typeof(RectTransform));
        RectTransform gripRect = grip.GetComponent<RectTransform>();
        gripRect.SetParent(areaRect, false);
        gripRect.offsetMin = Vector2.zero;
        gripRect.offsetMax = Vector2.zero;

        Image gripImage = grip.AddComponent<Image>();
        gripImage.color = new Color(1f, 1f, 1f, 0.45f);

        Scrollbar slider = bar.AddComponent<Scrollbar>();
        slider.direction = Scrollbar.Direction.BottomToTop;
        slider.handleRect = gripRect;
        slider.targetGraphic = gripImage;

        scroll.verticalScrollbar = slider;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        Debug.Log("Connect: built a scrollbar for the side list.");
    }

    private IEnumerator DiscoverServers(string address)
    {
        discoveryRunning = true;
        discovered.Clear();

        SetServersText($"Asking {ShortOnion(address)} for its server list...");

        string reply = null;
        bool reached = false;
        bool done = false;

        var thread = new Thread(() =>
        {
            try
            {
                reply = AskLine(address, TorConfig.MatchmakerPort, "GET_SERVERS");
                reached = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connect] Server list fetch failed: {e.Message}");
                reached = false;
            }
            finally { done = true; }
        });
        thread.IsBackground = true;
        thread.Start();
        while (!done) yield return null;

        if (!reached)
        {
            SetServersText($"Could not reach {ShortOnion(address)}.");
            discoveryRunning = false;
            yield break;
        }

        if (reply == null)
        {
            SetServersText($"{ShortOnion(address)} does not publish a server list.\n\n" +
                           "It is running an older Dragonator, or one without the registry add-on installed.");
            discoveryRunning = false;
            yield break;
        }

        foreach (string entry in reply.Split(';'))
        {
            Discovered row = ParseEntry(entry);
            if (row == null) continue;

            bool duplicate = false;
            foreach (Discovered existing in discovered)
                if (existing.Entry == row.Entry) { duplicate = true; break; }

            if (duplicate) continue;

            discovered.Add(row);
            if (discovered.Count >= MaxDiscovered) break;
        }

        if (discovered.Count == 0)
        {
            SetServersText($"{ShortOnion(address)} publishes a server list, but it is empty.\n\n" +
                           "No Dragonator has registered on the chain yet, or its registry is still catching up.");
            discoveryRunning = false;
            yield break;
        }

        int next = -1;
        int running = 0;
        int workers = Mathf.Min(ProbeWorkers, discovered.Count);

        for (int i = 0; i < workers; i++)
        {
            Interlocked.Increment(ref running);

            var worker = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        int index = Interlocked.Increment(ref next);
                        if (index >= discovered.Count) break;

                        Discovered row = discovered[index];

                        try
                        {
                            row.Info = AskLine(row.Onion, row.Port, "GET_SERVERINFO");
                            row.Online = true;
                        }
                        catch (Exception)
                        {
                            row.Online = false;
                        }
                        finally
                        {
                            row.Done = true;
                        }
                    }
                }
                finally { Interlocked.Decrement(ref running); }
            });

            worker.IsBackground = true;
            worker.Start();
        }

        float deadline = Time.time + DiscoveryTimeout;

        while (Volatile.Read(ref running) > 0 && Time.time < deadline)
        {
            RenderServers(address, false);
            yield return null;
        }

        RenderServers(address, true);
        discoveryRunning = false;
    }

    private void RenderServers(string source, bool finished)
    {
        int answered = 0;
        int checkedCount = 0;

        foreach (Discovered row in discovered)
        {
            if (!row.Done) continue;

            checkedCount++;
            if (row.Online) answered++;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append($"Servers known to {ShortOnion(source)}\n");

        sb.Append(finished
            ? $"{answered} of {discovered.Count} answered\n\n"
            : $"checking {checkedCount} of {discovered.Count}...\n\n");

        foreach (Discovered row in discovered)
        {
            sb.Append(row.Entry).Append('\n').Append("   ");

            if (!row.Done) sb.Append(finished ? "no answer" : "checking...");
            else if (!row.Online) sb.Append("no answer");
            else sb.Append("online - ").Append(DescribeOffer(row.Info));

            if (string.Equals(row.Onion, source.Trim(), StringComparison.OrdinalIgnoreCase))
                sb.Append("   (this server)");

            sb.Append("\n\n");
        }

        SetServersText(sb.ToString());
    }

    private string DescribeOffer(string wire)
    {
        if (wire == null) return "settings unavailable";
        if (wire.Trim().Length == 0) return "free match";

        decimal bet = -1m;
        bool swap = false;

        foreach (string pair in wire.Split(';'))
        {
            int split = pair.IndexOf('=');
            if (split <= 0) continue;

            string key = pair.Substring(0, split).Trim();
            string value = pair.Substring(split + 1).Trim();

            if (key == "bet")
            {
                decimal number;
                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                    bet = number;

                continue;
            }

            if (key == "swap")
                swap = value.Length > 0 &&
                       !value.Equals("off", StringComparison.OrdinalIgnoreCase) &&
                       !value.Equals("none", StringComparison.OrdinalIgnoreCase);
        }

        StringBuilder sb = new StringBuilder();
        sb.Append(bet > 0m ? $"bet {Format(bet)} XST" : "free match");
        if (swap) sb.Append(" · swap");

        return sb.ToString();
    }

    private static Discovered ParseEntry(string entry)
    {
        if (string.IsNullOrEmpty(entry)) return null;

        string trimmed = entry.Trim().ToLowerInvariant();

        int colon = trimmed.LastIndexOf(':');
        if (colon <= 0) return null;

        int port;
        if (!int.TryParse(trimmed.Substring(colon + 1), NumberStyles.Integer,
                          CultureInfo.InvariantCulture, out port)) return null;

        if (port <= 0 || port > 65535) return null;

        string host = trimmed.Substring(0, colon);
        if (!host.EndsWith(".onion", StringComparison.Ordinal)) return null;

        return new Discovered { Onion = host, Port = port };
    }

    private static string AskLine(string onion, int port, string command)
    {
        using (var cancel = new CancellationTokenSource(ProbeTimeoutMs))
        {
            var tcp = new TcpClient();
            tcp.ConnectThroughProxyAsync(TorConfig.SocksHost, TorConfig.SocksPort, onion, port,
                                         null, null, cancel.Token)
                .GetAwaiter().GetResult();

            using (tcp)
            using (var stream = tcp.GetStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                tcp.SendTimeout = ProbeTimeoutMs;
                tcp.ReceiveTimeout = ProbeTimeoutMs;

                writer.WriteLine(command);
                return reader.ReadLine();
            }
        }
    }

    private static string ShortOnion(string onion)
    {
        if (string.IsNullOrEmpty(onion)) return "the server";

        string host = onion.Trim();
        return host.Length <= 24 ? host : host.Substring(0, 8) + "..." + host.Substring(host.Length - 12);
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