using System;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class ClientConnectionState : ConnectionState
    {
        public Thread receiveThread;
        public bool Connected => client != null &&
                                 client.Client != null &&
                                 client.Client.Connected;

        public volatile bool Connecting;
        public readonly MagnificentReceivePipe receivePipe;
        public ClientConnectionState(int MaxMessageSize) : base(new TcpClient(), MaxMessageSize)
        {
            receivePipe = new MagnificentReceivePipe(MaxMessageSize);
        }
        public void Dispose()
        {
            client.Close();
            receiveThread?.Interrupt();
            Connecting = false;
            sendPipe.Clear();
            client = null;
        }
    }

    public class TorBaseClient : Common
    {
        public Action OnConnected;
        public Action<ArraySegment<byte>> OnData;
        public Action OnDisconnected;
        public int SendQueueLimit = 10000;
        public int ReceiveQueueLimit = 10000;
        protected ClientConnectionState state;

        // Connected & Connecting
        public bool Connected => state != null && state.Connected;
        public bool Connecting => state != null && state.Connecting;
        public int ReceivePipeCount => state != null ? state.receivePipe.TotalCount : 0;

        public TorBaseClient(int MaxMessageSize) : base(MaxMessageSize) { }
        protected static void ReceiveThreadFunction(ClientConnectionState state, string ip, int port, int MaxMessageSize, bool NoDelay, int SendTimeout, int ReceiveTimeout, int ReceiveQueueLimit)
        {
            Thread sendThread = null;

            try
            {
                if (!state.Connected)
                {
                    state.client.Connect(ip, port);
                }
                else
                {
                    Log.Info("[Tor] ReceiveThreadFunction: socket already connected, skipping Connect()");
                }

                state.Connecting = false;

                state.client.NoDelay = NoDelay;
                state.client.SendTimeout = SendTimeout;
                state.client.ReceiveTimeout = ReceiveTimeout;

                sendThread = new Thread(() =>
                {
                    ThreadFunctions.SendLoop(0, state.client, state.sendPipe, state.sendPending);
                });
                sendThread.IsBackground = true;
                sendThread.Start();

                ThreadFunctions.ReceiveLoop(0, state.client, MaxMessageSize, state.receivePipe, ReceiveQueueLimit);
            }
            catch (SocketException exception)
            {
                Log.Info("Client Recv: failed to connect to ip=" + ip + " port=" + port + " reason=" + exception);
            }
            catch (ThreadInterruptedException) { }
            catch (ThreadAbortException) { }
            catch (ObjectDisposedException) { }
            catch (Exception exception)
            {
                Log.Error("Client Recv Exception: " + exception);
            }

            state.receivePipe.Enqueue(0, EventType.Disconnected, default);
            sendThread?.Interrupt();
            state.Connecting = false;
            state.client?.Close();
        }

        public void Connect(string ip, int port)
        {
            if (Connecting || Connected)
            {
                Log.Warning("Telepathy Client can not create connection because an existing connection is connecting or connected");
                return;
            }
            state = new ClientConnectionState(MaxMessageSize);
            state.Connecting = true;
            state.receiveThread = new Thread(() =>
            {
                ReceiveThreadFunction(state, ip, port, MaxMessageSize, NoDelay, SendTimeout, ReceiveTimeout, ReceiveQueueLimit);
            });
            state.receiveThread.IsBackground = true;
            state.receiveThread.Start();
        }

        public void Disconnect()
        {
            if (Connecting || Connected)
            {
                state.Dispose();
            }
        }

        public bool Send(ArraySegment<byte> message)
        {
            if (Connected)
            {
                if (message.Count <= MaxMessageSize)
                {
                    // check send pipe limit
                    if (state.sendPipe.Count < SendQueueLimit)
                    {
                        state.sendPipe.Enqueue(message);
                        state.sendPending.Set(); // interrupt SendThread WaitOne()
                        return true;
                    }
                    else
                    {
                        Log.Warning($"Client.Send: sendPipe reached limit of {SendQueueLimit}. This can happen if we call send faster than the network can process messages. Disconnecting to avoid ever growing memory & latency.");
                        state.client.Close();
                        return false;
                    }
                }
                Log.Error("Client.Send: message too big: " + message.Count + ". Limit: " + MaxMessageSize);
                return false;
            }
            Log.Warning("Client.Send: not connected!");
            return false;
        }

        public int Tick(int processLimit, Func<bool> checkEnabled = null)
        {
            if (state == null)
                return 0;

            for (int i = 0; i < processLimit; ++i)
            {
                if (checkEnabled != null && !checkEnabled())
                    break;

                if (state.receivePipe.TryPeek(out int _, out EventType eventType, out ArraySegment<byte> message))
                {
                    switch (eventType)
                    {
                        case EventType.Connected:
                            OnConnected?.Invoke();
                            break;
                        case EventType.Data:
                            OnData?.Invoke(message);
                            break;
                        case EventType.Disconnected:
                            OnDisconnected?.Invoke();
                            break;
                    }
                    state.receivePipe.TryDequeue();
                }
                else break;
            }
            return state.receivePipe.TotalCount;
        }
    }
}
