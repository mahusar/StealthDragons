using System;
using System.Net.Sockets;
using System.Threading;           
using UnityEngine;
using Telepathy;

public class TorClient : TorBaseClient
{
    public TorClient(int maxMessageSize) : base(maxMessageSize)
    {
    }

    public void SetConnectedSocket(TcpClient connectedTcp)
    {
        if (state == null)
        {
            Debug.LogError("[TorClient] state is null - cannot inject socket");
            return;
        }

        if (state.client != null)
        {
            Debug.Log("[TorClient] Closing existing TcpClient");
            state.client.Close();
        }

        state.client = connectedTcp;
        Debug.Log("[TorClient] SOCKS5 TcpClient injected into state.client");
    }

    // Skip socket connect if already set
    public new void Connect(string ip, int port)
    {
        if (state == null)
        {
            base.Connect(ip, port); // normal path
            return;
        }

        if (state.client != null && state.client.Connected)
        {
            // Socket already connected via SOCKS5 — skip DNS, just start receive thread
            state.Connecting = false;
            state.receiveThread = new Thread(() =>
            {
                ReceiveThreadFunction(state, ip, port, MaxMessageSize, NoDelay, SendTimeout, ReceiveTimeout, ReceiveQueueLimit);
            });
            state.receiveThread.IsBackground = true;
            state.receiveThread.Start();
            Debug.Log("[TorClient] Using pre-connected SOCKS5 socket, skipping DNS");
            return;
        }

        base.Connect(ip, port);
    }
    public void PreConnect(TcpClient connectedTcp)
    {
        // Initialize state (mirrors what base.Connect() does before starting the thread)
        state = new ClientConnectionState(MaxMessageSize);
        state.Connecting = false;
        state.client = connectedTcp; // inject the SOCKS5 socket
    }
}