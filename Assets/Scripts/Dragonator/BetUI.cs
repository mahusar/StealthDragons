using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Mirror;
using System.Collections;

public class BetUI : MonoBehaviour
{
    public static BetUI Instance;

    [Header("Panel")]
    [SerializeField] private GameObject betPanel;

    [Header("Step 1 - payout address")]
    [SerializeField] private GameObject payoutStep;
    [SerializeField] private TMP_InputField payoutAddressInputField;
    [SerializeField] private Button submitButton;

    [Header("Step 2 - deposit")]
    [SerializeField] private GameObject depositStep;
    [SerializeField] private TMP_Text depositAddressText;
    [SerializeField] private Button copyAddressButton;
    [SerializeField] private TMP_Text betAmountText;
    [SerializeField] private TMP_Text countdownText;

    [Header("Shared")]
    [SerializeField] private TMP_Text statusText;

    [Header("Status Display")]
    [SerializeField] private GameObject statusBackground;
    [SerializeField] private TMP_Text player1StatusText;
    [SerializeField] private TMP_Text player2StatusText;

    private bool countdownRunning;
    private int lastRenderedSecond = -1;

    void Awake()
    {
        if (Utils.IsHeadless()) { gameObject.SetActive(false); return; }

        Instance = this;
        betPanel.SetActive(false);
        if (payoutStep != null) payoutStep.SetActive(false);
        if (depositStep != null) depositStep.SetActive(false);

        submitButton.onClick.AddListener(OnSubmitPayoutAddressClicked);
        copyAddressButton.onClick.AddListener(OnCopyAddressClicked);

        Debug.Log("[BetUI] Awake called, instance set.");
    }

    void Start()
    {
        StartCoroutine(WaitForLocalPlayerThenReady());
    }

    private IEnumerator WaitForLocalPlayerThenReady()
    {
        while (Player.localPlayer == null)
            yield return null;

        Debug.Log("[BetUI] Local player found, signaling ready.");

        DragonatorWallet wallet = FindFirstObjectByType<DragonatorWallet>();
        if (wallet != null)
            wallet.CmdClientReady();
        else
            Debug.LogError("[BetUI] DragonatorWallet not found in scene!");
    }

    void Update()
    {
        if (!countdownRunning || countdownText == null) return;

        DragonatorWallet wallet = DragonatorWallet.Instance;
        if (wallet == null || wallet.FundingDeadline <= 0d)
        {
            countdownText.text = "";
            countdownRunning = false;
            lastRenderedSecond = -1;
            return;
        }

        int remaining = Mathf.Max(0, Mathf.CeilToInt((float)(wallet.FundingDeadline - NetworkTime.time)));

        if (remaining == lastRenderedSecond) return;
        lastRenderedSecond = remaining;

        countdownText.text = $"{remaining / 60:0}:{remaining % 60:00} left to pay";
        countdownText.color = remaining <= 30 ? Color.red : Color.white;
    }

    public void ShowPayoutAddressStep(string amount, int confirmations)
    {
        betPanel.SetActive(true);
        if (payoutStep != null) payoutStep.SetActive(true);
        if (depositStep != null) depositStep.SetActive(false);

        submitButton.interactable = true;
        statusText.color = Color.white;
        statusText.text = $"Stake is {amount} XST ({confirmations} confirmations). " +
                          "Enter the XST address for your winnings or refund.";

        countdownRunning = true;
    }

    public void ShowDepositStep(string depositAddress, string amount)
    {
        betPanel.SetActive(true);
        if (payoutStep != null) payoutStep.SetActive(false);
        if (depositStep != null) depositStep.SetActive(true);

        depositAddressText.text = depositAddress;
        betAmountText.text = $"Send exactly {amount} XST";
        statusText.color = Color.white;
        statusText.text = "Waiting for your payment, it is detected automatically.";

        countdownRunning = true;
    }

    public void ShowFundingMessage(bool success, string message)
    {
        if (!betPanel.activeSelf)
        {
            betPanel.SetActive(true);
            if (payoutStep != null) payoutStep.SetActive(false);
            if (depositStep != null) depositStep.SetActive(false);
        }

        statusText.text = message;
        statusText.color = success ? Color.green : Color.red;

        if (!success && payoutStep != null && payoutStep.activeSelf)
            submitButton.interactable = true;
    }

    public void UpdatePlayerStatus(int player, string status)
    {
        if (player == 1)
        {
            if (player1StatusText != null) player1StatusText.text = status;
        }
        else
        {
            if (player2StatusText != null) player2StatusText.text = status;
        }
    }

    public void HideBetUI()
    {
        betPanel.SetActive(false);
        countdownRunning = false;
    }

    public void HideStatusDisplay()
    {
        if (player1StatusText != null) player1StatusText.gameObject.SetActive(false);
        if (player2StatusText != null) player2StatusText.gameObject.SetActive(false);
        if (statusBackground != null) statusBackground.SetActive(false);
    }

    public void ShowStatusDisplay()
    {
        betPanel.SetActive(true);
        if (payoutStep != null) payoutStep.SetActive(false);
        if (depositStep != null) depositStep.SetActive(false);

        if (statusBackground != null) statusBackground.SetActive(true);
        if (player1StatusText != null) player1StatusText.gameObject.SetActive(true);
        if (player2StatusText != null) player2StatusText.gameObject.SetActive(true);
    }

    private void OnSubmitPayoutAddressClicked()
    {
        string payoutAddress = payoutAddressInputField.text.Trim();

        if (string.IsNullOrEmpty(payoutAddress))
        {
            statusText.color = Color.red;
            statusText.text = "Enter your payout address.";
            return;
        }

        submitButton.interactable = false;
        statusText.color = Color.white;
        statusText.text = "Checking address...";

        DragonatorWallet wallet = FindFirstObjectByType<DragonatorWallet>();
        if (wallet != null)
            wallet.CmdSubmitPayoutAddress(payoutAddress);
        else
            Debug.LogError("[BetUI] DragonatorWallet not found - cannot submit payout address.");
    }

    private void OnCopyAddressClicked()
    {
        GUIUtility.systemCopyBuffer = depositAddressText.text;
        Debug.Log($"[BetUI] Address copied: {depositAddressText.text}");
        StartCoroutine(CopyFeedback());
    }

    private IEnumerator CopyFeedback()
    {
        TMP_Text label = copyAddressButton.GetComponentInChildren<TMP_Text>();
        if (label == null) yield break;

        string original = label.text;
        label.text = "Copied";
        yield return new WaitForSeconds(1.5f);
        label.text = original;
    }
}
