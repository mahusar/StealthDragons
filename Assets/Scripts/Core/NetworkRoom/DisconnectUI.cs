using UnityEngine;
using TMPro;

public class DisconnectUI : MonoBehaviour
{
    public GameObject disconnectPanel; // Reference to the UI GameObject (e.g., a panel)
    private TMP_Text messageText; // Reference to the TMP_Text child for dynamic messages

    void Start()
    {
        if (disconnectPanel != null)
        {
            messageText = disconnectPanel.GetComponentInChildren<TMP_Text>();
            if (messageText == null)
            {
                Debug.LogWarning("DisconnectUI: No TMP_Text component found in disconnectPanel's children.");
            }
            disconnectPanel.SetActive(false); // Hide panel at start
        }
        else
        {
            Debug.LogWarning("DisconnectUI: disconnectPanel is not assigned in the Inspector.");
        }
    }

    public void ShowDisconnectMessage(string message)
    {
        if (disconnectPanel != null)
        {
            if (messageText != null)
            {
                messageText.text = message;
                messageText.color = Color.yellow;
            }
            disconnectPanel.SetActive(true);
            Debug.Log($"DisconnectUI: Displaying '{message}' on panel");
        }
        else
        {
            Debug.LogWarning("DisconnectUI: disconnectPanel is null, cannot display message.");
        }
    }
}