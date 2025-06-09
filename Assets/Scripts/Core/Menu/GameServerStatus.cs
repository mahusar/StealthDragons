using UnityEngine;
using TMPro;
using System.Collections;
using System.Net.Sockets; // << important
using System;

public class GameServerStatus : MonoBehaviour
{
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private string serverAddress = "185.203.216.201";
    [SerializeField] private int serverPort = 7777;
    [SerializeField] private float checkInterval = 5f;
    [SerializeField] private int timeoutMilliseconds = 3000;

    private void Start()
    {
        if (statusText == null)
        {
            Debug.LogError("GameServerStatus: Status Text not assigned!");
            return;
        }
        StartCoroutine(CheckServerStatusLoop());
    }

    private IEnumerator CheckServerStatusLoop()
    {
        while (true)
        {
            yield return CheckServerStatus();
            yield return new WaitForSeconds(checkInterval);
        }
    }

    private IEnumerator CheckServerStatus()
    {
        bool isOnline = false;

        using (TcpClient client = new TcpClient())
        {
            var connectTask = client.ConnectAsync(serverAddress, serverPort);
            float timer = 0f;

            while (!connectTask.IsCompleted && timer < timeoutMilliseconds / 1000f)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            if (connectTask.IsCompleted && client.Connected)
            {
                isOnline = true;
            }
        }

        if (isOnline)
        {
            UpdateStatus("Online", Color.green);
            Debug.Log("Game server is online.");
        }
        else
        {
            UpdateStatus("Offline", Color.red);
            Debug.Log("Game server is offline or unreachable.");
        }
    }

    private void UpdateStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
    }
}
