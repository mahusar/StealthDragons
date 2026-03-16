using Mirror;
using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Socks5;
using Telepathy;

public class TorTelepathyTransport : TelepathyTransport
{
    [Header("Tor SOCKS5 Settings")]
    public string socksHost = "127.0.0.1";
    public int socksPort = 9050;

    // Our own tor client - completely separate from TelepathyTransport's 'client'
    private TorClient torClient;

    public override void ClientConnect(string hostname)
    {
        if (string.IsNullOrEmpty(hostname) || !hostname.EndsWith(".onion"))
        {
            base.ClientConnect(hostname);
            return;
        }

        Debug.Log("[Tor] .onion detected, connecting via SOCKS5...");

        // Create and wire up TorClient now, on main thread
        torClient = new TorClient(clientMaxMessageSize);
        torClient.NoDelay = NoDelay;
        torClient.SendTimeout = SendTimeout;
        torClient.ReceiveTimeout = ReceiveTimeout;
        torClient.SendQueueLimit = clientSendQueueLimit;
        torClient.ReceiveQueueLimit = clientReceiveQueueLimit;

        torClient.OnConnected = () => OnClientConnected.Invoke();
        torClient.OnData = (segment) => OnClientDataReceived.Invoke(segment, Channels.Reliable);
        torClient.OnDisconnected = () => OnClientDisconnected?.Invoke();

        var capturedHostname = hostname;
        var capturedPort = port;

        var thread = new Thread(() =>
        {
            try
            {
                var tcpClient = new TcpClient();
                var (boundAddress, boundPort) = tcpClient
                    .ConnectThroughProxyAsync(socksHost, socksPort, capturedHostname, capturedPort)
                    .GetAwaiter()
                    .GetResult();

                Debug.Log($"[Tor] SOCKS5 tunnel established, bound {boundAddress}:{boundPort}");

                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    Debug.Log("[Tor] Injecting socket into TorClient...");
                    torClient.PreConnect(tcpClient);
                    torClient.Connect(capturedHostname, capturedPort);
                    Debug.Log("[Tor] TorClient started");
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Tor] SOCKS5 failed: {ex.Message}\n{ex.StackTrace}");
            }
        });
        thread.IsBackground = true;
        thread.Start();
    }

    // Route all client calls to torClient if active, otherwise base
    public override bool ClientConnected() =>
        torClient != null ? torClient.Connected : base.ClientConnected();

    public override void ClientSend(ArraySegment<byte> segment, int channelId)
    {
        if (torClient != null)
        {
            torClient.Send(segment);
            OnClientDataSent?.Invoke(segment, Channels.Reliable);
        }
        else
        {
            base.ClientSend(segment, channelId);
        }
    }

    public override void ClientDisconnect()
    {
        if (torClient != null)
        {
            torClient.Disconnect();
            torClient = null;
            OnClientDisconnected?.Invoke();
        }
        else
        {
            base.ClientDisconnect();
        }
    }

    public override void ClientEarlyUpdate()
    {
        if (!enabled) return;

        if (torClient != null)
            torClient.Tick(clientMaxReceivesPerTick, () => enabled);
        else
            base.ClientEarlyUpdate();
    }
}