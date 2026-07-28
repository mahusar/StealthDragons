using Mirror;
using UnityEngine;

public class DragonRoomPlayer : NetworkRoomPlayer
{
    [SyncVar] public string username = PlayerName.Default;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        CmdSetUsername(PlayerName.Resolve());
    }

    [Command]
    public void CmdSetUsername(string name)
    {
        username = PlayerName.Sanitize(name);
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