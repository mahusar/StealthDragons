using UnityEngine;
using Mirror;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class Chat : NetworkBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TMP_Text chatHistory;  
    [SerializeField] private Scrollbar scrollbar;
    [SerializeField] private TMP_InputField chatMessage;
    // This is only set on client to the name of the local player
    internal static string localPlayerName;

    // Server-only cross-reference of connections to player names
    internal static readonly Dictionary<NetworkConnectionToClient, string> connNames = new Dictionary<NetworkConnectionToClient, string>();

    const int maxMessageLength = 200;
    const int maxNameLength = 32;
    const int maxMessagesPerWindow = 4;
    const float rateWindowSeconds = 4f;
    const int maxHistoryLines = 100;

    class SendRate
    {
        public float windowStart;
        public int count;
        public bool throttleLogged;
    }

    static readonly Dictionary<NetworkConnectionToClient, SendRate> sendRates = new Dictionary<NetworkConnectionToClient, SendRate>();

    readonly Queue<string> historyLines = new Queue<string>();

    private GameManager gameManager;
    private bool chatDisabled;

    public override void OnStartServer()
    {
        connNames.Clear();
        sendRates.Clear();
    }

    public override void OnStartClient()
    {
        if (chatHistory != null) chatHistory.text = "";
    }

    void Update()
    {
        if (chatDisabled) return;

        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager == null || !gameManager.practiceMode) return;

        DisableChat();
    }

    private void DisableChat()
    {
        chatDisabled = true;

        foreach (Transform child in transform)
            child.gameObject.SetActive(false);

        Debug.Log("Chat: disabled for this practice match.");
    }

    [Server]
    private bool IsPracticeMatch()
    {
        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
        return gameManager != null && gameManager.practiceMode;
    }

    static string Sanitize(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";

        StringBuilder clean = new StringBuilder(Mathf.Min(value.Length, maxLength));
        foreach (char c in value)
        {
            if (clean.Length >= maxLength) break;
            if (c == '<' || c == '>' || c == '\n' || c == '\r') continue;
            if (char.IsControl(c)) continue;
            clean.Append(c);
        }
        return clean.ToString().Trim();
    }

    [Server]
    bool WithinSendRate(NetworkConnectionToClient sender)
    {
        if (!sendRates.TryGetValue(sender, out SendRate rate))
        {
            rate = new SendRate { windowStart = Time.unscaledTime, count = 0 };
            sendRates[sender] = rate;
        }

        if (Time.unscaledTime - rate.windowStart >= rateWindowSeconds)
        {
            rate.windowStart = Time.unscaledTime;
            rate.count = 0;
            rate.throttleLogged = false;
        }

        if (rate.count >= maxMessagesPerWindow)
        {
            if (!rate.throttleLogged)
            {
                rate.throttleLogged = true;
                Debug.LogWarning($"Chat: throttling connection {sender.connectionId} - more than {maxMessagesPerWindow} messages in {rateWindowSeconds}s.");
            }
            return false;
        }

        rate.count++;
        return true;
    }

    [Server]
    string ResolveSenderName(NetworkConnectionToClient sender)
    {
        if (connNames.TryGetValue(sender, out string cached)) return cached;

        Player player = sender.identity != null ? sender.identity.GetComponent<Player>() : null;
        string resolved = Sanitize(player != null ? player.username : null, maxNameLength);

        if (resolved.Length == 0) return $"Player {sender.connectionId}";

        connNames[sender] = resolved;
        return resolved;
    }

    [Command(requiresAuthority = false)]
    void CmdSend(string message, NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;
        if (IsPracticeMatch()) return;
        if (!WithinSendRate(sender)) return;

        string clean = Sanitize(message, maxMessageLength);
        if (clean.Length == 0) return;

        RpcReceive(ResolveSenderName(sender), clean);
    }

    [ClientRpc]
    void RpcReceive(string playerName, string message)
    {
        string safeName = Sanitize(playerName, maxNameLength);
        string safeMessage = Sanitize(message, maxMessageLength);
        if (safeMessage.Length == 0) return;

        AppendMessage($"<color=grey>{safeName}:</color> {safeMessage}");
    }

    void AppendMessage(string message)
    {
        if (chatHistory == null) return;

        historyLines.Enqueue(message);
        while (historyLines.Count > maxHistoryLines)
            historyLines.Dequeue();

        chatHistory.text = string.Join("\n", historyLines);

        StartCoroutine(ScrollToBottom());
    }

    IEnumerator ScrollToBottom()
    {
        // it takes 2 frames for the UI to update ?!?!
        yield return null;
        yield return null;

        // slam the scrollbar down
        if (scrollbar != null) scrollbar.value = 0;
    }

    // Called by UI element ExitButton.OnClick
    public void ExitButtonOnClick()
    {
        XSTDragonNetworkManager manager = NetworkManager.singleton as XSTDragonNetworkManager;
        if (manager == null)
        {
            Debug.LogError("Chat: XSTDragonNetworkManager not found - cannot leave the match.");
            return;
        }

        manager.LeaveCurrentSession();
    }

    // Called by UI element MessageField.OnValueChanged
    public void ToggleButton(string input)
    {
     //   sendButton.interactable = !string.IsNullOrWhiteSpace(input);
    }

    // Called by UI element MessageField.OnEndEdit
    public void OnEndEdit(string input)
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetButtonDown("Submit"))
            SendMessage();
    }

    // Called by OnEndEdit above and UI element SendButton.OnClick
    public void SendMessage()
    {
        if (!string.IsNullOrWhiteSpace(chatMessage.text))
        {
            CmdSend(chatMessage.text.Trim());
            chatMessage.text = string.Empty;
            chatMessage.ActivateInputField();
        }
    }
}


