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
        if (gameManager == null || statusText == null || Player.localPlayer == null)
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
        }
        else
        {
            outcomePanel.SetActive(false);
        }
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