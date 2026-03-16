using UnityEngine;
using TMPro;

public class XSTDragonNetworkUI : MonoBehaviour
{
    public TMP_InputField usernameInput;
    string username = "StealthDragon";

    void Awake()
    {
        if (usernameInput != null)
            usernameInput.onEndEdit.AddListener(OnUsernameEntered);
    }

    void Start()
    {
        username = PlayerPrefs.GetString("Name", "StealthDragon");
        if (usernameInput != null)
            usernameInput.text = username;
    }

    void OnUsernameEntered(string input)
    {
        username = string.IsNullOrEmpty(input.Trim()) ? "StealthDragon" : input.Trim();
        usernameInput.text = username;
        PlayerPrefs.SetString("Name", username);
        PlayerPrefs.Save();
    }
}