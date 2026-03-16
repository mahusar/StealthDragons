using Mirror;
using UnityEngine;

public class DragonRoomPlayer : NetworkRoomPlayer
{
    [SyncVar] public string username = "StealthDragon";

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        CmdSetUsername(PlayerPrefs.GetString("Name", "StealthDragon"));
    }

    [Command]
    void CmdSetUsername(string name)
    {
        username = string.IsNullOrEmpty(name) ? "StealthDragon" : name;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"DragonRoomPlayer started for client {netId}");
    }

    public override void OnClientEnterRoom()
    {
        base.OnClientEnterRoom();
        RoomUIManager.Instance?.UpdatePlayerList();
    }

    public override void OnClientExitRoom()
    {
        base.OnClientExitRoom();
    }

    public override void ReadyStateChanged(bool oldReadyState, bool newReadyState)
    {
        base.ReadyStateChanged(oldReadyState, newReadyState);
        RoomUIManager.Instance?.UpdatePlayerList();
    }
}