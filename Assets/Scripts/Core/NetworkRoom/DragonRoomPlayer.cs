// DragonRoomPlayer.cs (unchanged)
using Mirror;
using UnityEngine;

public class DragonRoomPlayer : NetworkRoomPlayer
{
    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"DragonRoomPlayer started for client {netId}");
    }

    public override void OnClientEnterRoom()
    {
        base.OnClientEnterRoom();
        Debug.Log($"Player {netId} entered room");
    }

    public override void OnClientExitRoom()
    {
        base.OnClientExitRoom();
        Debug.Log($"Player {netId} exited room");
    }

    public override void ReadyStateChanged(bool oldReadyState, bool newReadyState)
    {
        base.ReadyStateChanged(oldReadyState, newReadyState);
        RoomUIManager.Instance?.UpdatePlayerList();
        Debug.Log($"Player {netId} ready state changed to {newReadyState}");
    }
}

// RoomInfo.cs (unchanged)
public class RoomInfo
{
    public string roomName;
    public string ip;
    public int port;
    public float lastActiveTime;
    public int playerCount; // New field

    public RoomInfo(string roomName, string ip, int port)
    {
        this.roomName = roomName;
        this.ip = ip;
        this.port = port;
        this.lastActiveTime = Time.time;
        this.playerCount = 1; // Starts with host
    }

    public string GetStatus()
    {
        return playerCount < 2 ? "Open" : "Closed";
    }
}