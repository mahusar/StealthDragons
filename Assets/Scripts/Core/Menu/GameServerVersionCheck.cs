using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System.Collections;

public class GameServerVersionCheck : MonoBehaviour
{
    [Header("Version Settings")]
    [SerializeField] private string versionUrl = "http://185.203.216.201/stealthdragons/version.txt";
    [SerializeField] private string currentVersion = "0";

    [Header("UI References")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text versionText;

    [Header("GameObjects to Toggle")]
    [SerializeField] private GameObject menuPanel; 
    [SerializeField] private GameObject updatePanel; 

    private void Start()
    {
        if (versionText != null)
            versionText.text = $"Version: {currentVersion}";

        StartCoroutine(CheckServerVersion());
    }

    private IEnumerator CheckServerVersion()
    {
        UnityWebRequest request = UnityWebRequest.Get(versionUrl);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            UpdateStatus("Error", Color.yellow);
            DisableMenu();
        }
        else
        {
            string serverVersion = request.downloadHandler.text.Trim();
            CompareVersions(serverVersion);
        }
    }

    private void CompareVersions(string serverVersion)
    {
        if (serverVersion == currentVersion)
        {
            UpdateStatus("Updated", Color.green);
            EnableMenu();
        }
        else
        {
            UpdateStatus("Outdated", Color.red);
            DisableMenu();
            ShowUpdateMessage();
        }
    }

    private void UpdateStatus(string status, Color color)
    {
        if (statusText != null)
        {
            statusText.text = status;
            statusText.color = color;
        }
    }

    private void EnableMenu()
    {
        if (menuPanel != null)
            menuPanel.SetActive(true);

        if (updatePanel != null)
            updatePanel.SetActive(false);

        Debug.Log("Menu Enabled.");
    }

    private void DisableMenu()
    {
        if (menuPanel != null)
            menuPanel.SetActive(false);

        Debug.Log("Menu Disabled.");
    }

    private void ShowUpdateMessage()
    {
        if (updatePanel != null)
            updatePanel.SetActive(true);

        Debug.Log("Please update your game.");
    }
}
