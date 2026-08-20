using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Collections;
using TMPro;

public class OutcomeUI : MonoBehaviour
{
    [Header("Main Panel")]
    [SerializeField] private GameObject outcomePanel; 

    [Header("Outcome")]
    [SerializeField] private TMP_Text statusText;

    [Header("Fairness")]
    [SerializeField] private TMP_Text fairnessText;
    [SerializeField] private Color fairnessPassColor = new Color(0.45f, 0.85f, 0.45f);
    [SerializeField] private Color fairnessPartialColor = new Color(0.95f, 0.8f, 0.35f);
    [SerializeField] private Color fairnessFailColor = new Color(1f, 0.35f, 0.35f);

    [Header("Practice")]
    [SerializeField] private Button resetButton;

    [Header("Winner TXID")]
    [SerializeField] private GameObject txidPanel;
    [SerializeField] private TMP_InputField txidInputField;
    [SerializeField] private Button copyTxidButton;

    public GameManager gameManager;

    void Start()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();

        outcomePanel.SetActive(false); 
        txidPanel.SetActive(false);

        if (resetButton != null)
        {
            resetButton.gameObject.SetActive(false);
            resetButton.onClick.AddListener(OnResetClicked);
        }

        StartCoroutine(InitializeOutcomeDisplay());
    }

    private IEnumerator InitializeOutcomeDisplay()
    {
        while (Player.localPlayer == null)
            yield return null;

        UpdateOutcomeDisplay();
    }

    public void UpdateOutcomeDisplay()
    {
        if (gameManager == null || statusText == null || outcomePanel == null || Player.localPlayer == null)
        {
            Debug.LogWarning("[OutcomeUI] Missing reference.");
            return;
        }

        var localPlayerOutcome = gameManager.gameOutcomes
            .Where(o => o.netId == Player.localPlayer.netIdentity.netId)
            .OrderByDescending(o => gameManager.gameOutcomes.IndexOf(o))
            .FirstOrDefault();

        if (localPlayerOutcome.username != null)
        {
            outcomePanel.SetActive(true);
            statusText.text = localPlayerOutcome.isWinner ? "Victory" : "Defeat";
            statusText.color = localPlayerOutcome.isWinner ? Color.green : Color.red;
            ShowFairness();
        }
        else
        {
            outcomePanel.SetActive(false);
        }
    }

    private bool Practicing()
    {
        return gameManager != null && gameManager.practiceMode;
    }

    public void ShowFairness()
    {
        if (resetButton != null && resetButton.gameObject.activeSelf != Practicing())
            resetButton.gameObject.SetActive(Practicing());

        if (fairnessText == null) return;

        if (Practicing())
        {
            fairnessText.text = "";
            return;
        }

        if (!LocalShuffleProof.Checked)
        {
            fairnessText.text = "";
            return;
        }

        if (!LocalShuffleProof.Passed)
        {
            fairnessText.color = fairnessFailColor;
            fairnessText.text = "THIS MATCH DID NOT VERIFY\n" + LocalShuffleProof.Result;
            return;
        }

        bool signed = gameManager != null && gameManager.ReceiptFullySigned();
        bool everyHand = LocalShuffleProof.Unverified == 0;

        string deal = everyHand
            ? "Match verified - every hand matches the committed seed"
            : $"Match verified - {LocalShuffleProof.Unverified} hand(s) unchecked";

        fairnessText.color = everyHand && signed ? fairnessPassColor : fairnessPartialColor;
        fairnessText.text = deal + "\n" + ReceiptLine(signed);
    }

    private string ReceiptLine(bool signed)
    {
        if (gameManager == null || string.IsNullOrEmpty(gameManager.matchReceipt))
            return "No match receipt was issued";

        MatchReceipt receipt = MatchReceipt.Parse(gameManager.matchReceipt);
        if (receipt == null) return "The match receipt could not be read";

        string digest = receipt.DigestHex();
        if (digest.Length > 16) digest = digest.Substring(0, 16);

        return signed
            ? $"Receipt {digest} signed by every player"
            : $"Receipt {digest} is NOT signed by every player";
    }

    private void OnResetClicked()
    {
        PracticeMode practice = PracticeMode.Instance;

        if (practice == null)
        {
            Debug.LogWarning("[OutcomeUI] No PracticeMode to restart.");
            return;
        }

        if (outcomePanel != null) outcomePanel.SetActive(false);

        practice.Restart();
    }

    public void ShowWinnerTxid(string txid)
    {
        txidPanel.SetActive(true);
        txidInputField.text = txid;
        txidInputField.readOnly = true;
        copyTxidButton.onClick.AddListener(() =>
        {
            GUIUtility.systemCopyBuffer = txid;
            StartCoroutine(CopyTxidFeedback());
        });
    }

    private IEnumerator CopyTxidFeedback()
    {
        string original = copyTxidButton.GetComponentInChildren<TMP_Text>().text;
        copyTxidButton.GetComponentInChildren<TMP_Text>().text = "Copied!";
        yield return new WaitForSeconds(1.5f);
        copyTxidButton.GetComponentInChildren<TMP_Text>().text = original;
    }
}