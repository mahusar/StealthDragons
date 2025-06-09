using UnityEngine;
using Mirror;
using TMPro;

public class XSTDragonNetworkUI : MonoBehaviour
{
    NetworkManager manager;

    public TMP_InputField usernameInput;
    string username = "";

    void Awake()
    {
        manager = GetComponent<NetworkManager>();

        // Hook up Enter key saving
        if (usernameInput != null)
        {
            usernameInput.onEndEdit.AddListener(OnUsernameEntered);
        }
    }

    void Start()
    {
        if (PlayerPrefs.HasKey("Name"))
        {
            username = PlayerPrefs.GetString("Name");
            if (usernameInput != null)
                usernameInput.text = username;
        }
    }

    void OnUsernameEntered(string input)
    {
        // Save to PlayerPrefs
        username = input;
        PlayerPrefs.SetString("Name", username);
        PlayerPrefs.Save();
        Debug.Log("Username saved: " + username);
    }

    public void OnClickHost()
    {
        if (usernameInput != null)
            username = usernameInput.text;

        PlayerPrefs.SetString("Name", username);
        manager.StartHost();
    }

    public void OnClickClient()
    {
        if (usernameInput != null)
            username = usernameInput.text;

        PlayerPrefs.SetString("Name", username);
        manager.StartClient();
    }
}
