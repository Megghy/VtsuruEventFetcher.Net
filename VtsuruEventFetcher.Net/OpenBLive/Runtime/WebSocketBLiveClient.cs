using OpenBLive.Client.Data;
using OpenBLive.Runtime.Data;
using System.Net.WebSockets;
using Websocket.Client;
using OpenBLive.Runtime.Utilities;

namespace OpenBLive.Runtime
{
    public class WebSocketBLiveClient : BLiveClient
    {
        bool _disposed = false;
        /// <summary>
        ///  wss 长连地址
        /// </summary>
        public IList<string> WssLink;
        public AppStartData AuthInfo;

        public WebsocketClient clientWebSocket;

        public WebSocketBLiveClient(AppStartInfo info)
        {
            var websocketInfo = info.Data.WebsocketInfo;
            AuthInfo = info.Data;

            WssLink = websocketInfo.WssLink;
            token = websocketInfo.AuthBody;
        }
        public WebSocketBLiveClient(AppStartData info)
        {
            var websocketInfo = info.WebsocketInfo;
            AuthInfo = info;

            WssLink = websocketInfo.WssLink;
            token = websocketInfo.AuthBody;
        }

        public WebSocketBLiveClient(IList<string> wssLink, string authBody)
        {
            WssLink = wssLink;
            token = authBody;
        }


        public override async Task<bool> Connect()
        {
            var url = WssLink.FirstOrDefault();
            if (string.IsNullOrEmpty(url))
            {
                throw new Exception("wsslink is invalid");
            }
            Disconnect();

            clientWebSocket = new WebsocketClient(new Uri(url));
            clientWebSocket.MessageReceived.Subscribe(e =>
            ProcessPacket(e.Binary));
            clientWebSocket.DisconnectionHappened.Subscribe(e =>
            {
                if (_disposed)
                {
                    return;
                }
                if (e.Type == DisconnectionType.ByUser)
                {
                    Console.WriteLine("WS CLOSED BY USER");
                }
                else if (e.CloseStatus == WebSocketCloseStatus.Empty)
                    Console.WriteLine("WS CLOSED");
                else
                {
                    Console.WriteLine("WS ERROR: " + e.Exception?.Message);
                    Dispose();
                }
            });

            await clientWebSocket.Start();
            if (clientWebSocket.IsStarted)
            {
                OnOpen();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 带有重连
        /// </summary>
        /// <param name="timeout">ReconnectTimeout ErrorReconnectTimeout</param>
        public override async Task<bool> Connect(TimeSpan timeout)
        {
            var url = WssLink.FirstOrDefault();
            if (string.IsNullOrEmpty(url))
            {
                throw new Exception("wsslink is invalid");
            }
            clientWebSocket?.Stop(WebSocketCloseStatus.Empty, string.Empty);
            clientWebSocket?.Dispose();

            clientWebSocket = new WebsocketClient(new Uri(url));
            clientWebSocket.MessageReceived.Subscribe(e =>
            {
                //Console.WriteLine(e.Binary.Length);
                ProcessPacket(e.Binary);
            });
            clientWebSocket.DisconnectionHappened.Subscribe(e =>
            {
                if (_disposed)
                {
                    return;
                }
                if (e.Type == DisconnectionType.ByUser)
                {
                    Console.WriteLine("WS CLOSED BY USER");
                }
                else if (e.CloseStatus == WebSocketCloseStatus.Empty)
                    Console.WriteLine("WS CLOSED");
                else if (e?.Exception != null)
                {
                    Console.WriteLine("WS ERROR: " + e?.Exception?.Message);
                    Dispose();
                }
            });
            await clientWebSocket.Start();
            clientWebSocket.IsReconnectionEnabled = true;
            clientWebSocket.ReconnectTimeout = timeout;
            clientWebSocket.ErrorReconnectTimeout = timeout;
            clientWebSocket.ReconnectionHappened.Subscribe(e =>
            {
                SendAsync(Packet.Authority(token));
            });
            if (clientWebSocket.IsStarted)
            {
                OnOpen();
                return true;
            }

            return false;
        }

        public override void Disconnect()
        {
            if (clientWebSocket is not null)
            {
                OnClose();
                if (clientWebSocket?.IsRunning == true)
                {
                    _ = (clientWebSocket?.Stop(WebSocketCloseStatus.Empty, string.Empty));
                }
                clientWebSocket?.Dispose();
            }
            clientWebSocket = null;
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Disconnect();
            GC.SuppressFinalize(this);
        }
        int errorCount = 0;
        public override void Send(byte[] packet)
        {
            try
            {
                clientWebSocket?.Send(packet);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex.Message);
                errorCount++;
                if (errorCount > 5)
                {
                    Dispose();
                }
            }
        }


        public override void Send(Packet packet) => Send(packet.ToBytes);
        public override Task SendAsync(byte[] packet) => Task.Run(() => Send(packet));
        protected override Task SendAsync(Packet packet) => SendAsync(packet.ToBytes);
    }
}