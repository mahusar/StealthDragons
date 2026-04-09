using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Mirror;
using System.Collections;

public class BetUI : MonoBehaviour
{
    public static BetUI Instance;

    [Header("UI")]
    [SerializeField] private GameObject betPanel;
    [SerializeField] private TMP_Text depositAddressText;
    [SerializeField] private Button copyAddressButton;
    [SerializeField] private TMP_Text betAmountText;
    [SerializeField] private TMP_InputField txidInputField;
    [SerializeField] private TMP_InputField payoutAddressInputField;
    [SerializeField] private Button submitButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private TMP_Text statusText;
    [Header("Status Display")]
    [SerializeField] private GameObject statusBackground;
    [SerializeField] private TMP_Text player1StatusText;
    [SerializeField] private TMP_Text player2StatusText;

    void Awake()
    {
        if (Utils.IsHeadless()) { gameObject.SetActive(false); return; }
        Instance = this;
        betPanel.SetActive(false);
        submitButton.onClick.AddListener(OnSubmitClicked);
        copyAddressButton.onClick.AddListener(OnCopyAddressClicked);
        Debug.Log("[BetUI] Awake called, instance set.");
        retryButton.onClick.AddListener(OnRetryClicked);
        retryButton.gameObject.SetActive(false);
    }
    void Start()
    {
        StartCoroutine(WaitForLocalPlayerThenReady());
    }

    private IEnumerator WaitForLocalPlayerThenReady()
    {
        // Wait until local player exists on the network
        while (Player.localPlayer == null)
            yield return null;

        Debug.Log("[BetUI] Local player found, signaling ready.");

        DragonatorWallet wallet = FindFirstObjectByType<DragonatorWallet>();
        if (wallet != null)
            wallet.CmdClientReady();
        else
            Debug.LogError("[BetUI] DragonatorWallet not found in scene!");
    }

    public void UpdatePlayerStatus(int player, string status)
    {
        if (player == 1) player1StatusText.text = $"Player 1: {status}";
        else player2StatusText.text = $"Player 2: {status}";
    }
    public void ShowBetUI(string address, float amount)
    {
        betPanel.SetActive(true);
        depositAddressText.text = address;
        betAmountText.text = $"Send exactly {amount} XST";
        statusText.text = "Waiting for your transaction...";
    }

    public void HideBetUI()
    {
        betPanel.SetActive(false);
    }
    public void HideStatusDisplay()
    {
        if (player1StatusText != null) player1StatusText.gameObject.SetActive(false);
        if (player2StatusText != null) player2StatusText.gameObject.SetActive(false);
        if (statusBackground != null) statusBackground.SetActive(false);
    }

    public void ShowValidationResult(bool success, string message)
    {
        statusText.text = message;
        statusText.color = success ? Color.green : Color.red;

        if (success)
        {
            submitButton.interactable = false;
            retryButton.gameObject.SetActive(false);
        }
        else
        {
            submitButton.interactable = false;
            retryButton.gameObject.SetActive(true);
        }
    }

    private void OnSubmitClicked()
    {
        string txid = txidInputField.text.Trim();
        string payoutAddress = payoutAddressInputField.text.Trim();

        if (string.IsNullOrEmpty(txid))
        {
            statusText.text = "Enter your TXID first.";
            return;
        }
        if (string.IsNullOrEmpty(payoutAddress))
        {
            statusText.text = "Enter your payout address.";
            return;
        }

        submitButton.interactable = false;
        statusText.text = "Validating...";

        DragonatorWallet wallet = FindFirstObjectByType<DragonatorWallet>();
        if (wallet != null)
            wallet.CmdSubmitTxid(txid, payoutAddress);
    }

    private void OnRetryClicked()
    {
        txidInputField.text = "";
        payoutAddressInputField.text = "";
        submitButton.interactable = true;
        retryButton.gameObject.SetActive(false);
        statusText.text = "Waiting for your transaction...";
        statusText.color = Color.white;
    }
    private void OnCopyAddressClicked()
    {
        GUIUtility.systemCopyBuffer = depositAddressText.text;
        Debug.Log($"[BetUI] Address copied: {depositAddressText.text}");
        // Optional: show feedback
        StartCoroutine(CopyFeedback());
    }

    private IEnumerator CopyFeedback()
    {
        string original = copyAddressButton.GetComponentInChildren<TMP_Text>().text;
        copyAddressButton.GetComponentInChildren<TMP_Text>().text = "Copied";
        yield return new WaitForSeconds(1.5f);
        copyAddressButton.GetComponentInChildren<TMP_Text>().text = original;
    }
}