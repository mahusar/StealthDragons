using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Collections;
using TMPro;

public class OutcomeUI : MonoBehaviour
{
    public TMP_Text statusText; // Reference to Text component
    public GameObject statusBackgroundObject; // Reference to StatusBackground GameObject
    public GameManager gameManager;

    void Start()
    {
        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();

        if (statusText != null)
        {
            statusText.enabled = false; // Disable text at start
            statusText.text = "";
        }
        if (statusBackgroundObject != null)
        {
            statusBackgroundObject.SetActive(false); // Disable background at start
        }

        // Start coroutine to wait for Player.localPlayer
        StartCoroutine(InitializeOutcomeDisplay());
    }

    private IEnumerator InitializeOutcomeDisplay()
    {
        // Wait until Player.localPlayer is set
        while (Player.localPlayer == null)
        {
            yield return null;
        }

        UpdateOutcomeDisplay();
    }

    public void UpdateOutcomeDisplay()
    {
        if (gameManager == null || statusText == null || Player.localPlayer == null)
        {
            return;
        }

        // Find the most recent outcome for the local player
        var localPlayerOutcome = gameManager.gameOutcomes
            .Where(o => o.netId == Player.localPlayer.netIdentity.netId)
            .OrderByDescending(o => gameManager.gameOutcomes.IndexOf(o)) // Most recent first
            .FirstOrDefault();

        if (localPlayerOutcome.username != null)
        {
            statusText.text = localPlayerOutcome.isWinner ? "Victory" : "Defeat";
            statusText.enabled = true;
            statusText.color = localPlayerOutcome.isWinner ? Color.green : Color.red;
            if (statusBackgroundObject != null)
                statusBackgroundObject.SetActive(true);
        }
        else
        {
            statusText.text = "";
            statusText.enabled = false;
            if (statusBackgroundObject != null)
                statusBackgroundObject.SetActive(false);
        }
    }
}