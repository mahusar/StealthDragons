using Mirror;
using UnityEngine;
using TMPro;

public class XSTDragonNetworkUI : MonoBehaviour
{
    public TMP_InputField usernameInput;
    string username = PlayerName.Default;

    void Awake()
    {
        if (usernameInput != null)
        {
            usernameInput.onEndEdit.AddListener(OnUsernameEntered);
            usernameInput.onValueChanged.AddListener(OnUsernameTyped);
        }
    }

    void Start()
    {
        username = PlayerName.Resolve();
        if (usernameInput != null)
            usernameInput.text = username;
    }

    void OnUsernameTyped(string input)
    {
        username = PlayerName.Sanitize(input);
        PlayerName.Remember(username);
    }

    void OnUsernameEntered(string input)
    {
        username = PlayerName.Sanitize(input);
        usernameInput.text = username;
        PlayerName.Save(username);
        PushToRoomPlayer(username);
    }

    void PushToRoomPlayer(string name)
    {
        if (!NetworkClient.active || NetworkClient.localPlayer == null) return;

        DragonRoomPlayer roomPlayer = NetworkClient.localPlayer.GetComponent<DragonRoomPlayer>();
        if (roomPlayer != null && roomPlayer.isOwned)
        {
            roomPlayer.CmdSetUsername(name);
            Debug.Log($"[XSTDragonNetworkUI] Pushed renamed player '{name}' to the room.");
        }
    }
}
