using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenBLive.Client.Data;
using OpenBLive.Runtime;

namespace VtsuruEventFetcher.Net.DanmakuClient
{
    public class OpenLiveClient : IDanmakuClient
    {
        private readonly EventFetcher _fetcher;
        bool _isRunning = false;
        bool _isDisposed = false;

        AppStartData _authInfo;
        WebSocketBLiveClient _chatClient;
        System.Timers.Timer _timer;
        public OpenLiveClient(EventFetcher fetcher)
        {
            _fetcher = fetcher;
        }
        public async Task Init()
        {
            _authInfo = await StartRoomAsync(_fetcher.VTsuruToken);
            while (_authInfo == null && !_isDisposed)
            {
                _fetcher.Log("[OpenLive] 初始化失败, 10秒后重试");
                await Task.Delay(10000);
                _authInfo ??= await StartRoomAsync(_fetcher.VTsuruToken);
            }

            if (_timer is null)
            {
                // Starting the Heartbeat
                _timer ??= new()
                {
                    Interval = 20 * 1000,
                    AutoReset = true
                };
                _timer.Elapsed += (_, _) => _ = SendHeartbeatAsync();
                _timer.Start();
            }

        }
        bool isConnecting = false;
        public async Task Connect()
        {
            if (isConnecting)
            {
                return;
            }
            isConnecting = true;
            //创建websocket客户端
            try
            {
                var WebSocketBLiveClient = new WebSocketBLiveClient(_authInfo.WebsocketInfo.WssLink, _authInfo.WebsocketInfo.AuthBody);
                WebSocketBLiveClient.ReceiveNotice += WebSocketBLiveClient_ReceiveNotice;
                //连接长链  需自己处理重连
                //m_WebSocketBLiveClient.Connect();
                //连接长链 带有自动重连
                WebSocketBLiveClient.Close += (_, _) =>
                {
                    _ = Task.Run(OnClose);
                };
                var success = await WebSocketBLiveClient.Connect();
                if (!success)
                {
                    isConnecting = false;
                    throw new("[OpenLive] 无法连接至房间");
                }
                else
                {
                    _fetcher.Log($"[OpenLive] 已连接直播间: {_authInfo.AnchorInfo.UName}<{_authInfo.AnchorInfo.Uid}>");
                    _fetcher.Errors.Remove(ErrorCodes.CLIENT_DISCONNECTED);
                    _chatClient = WebSocketBLiveClient;
                    _isRunning = true;
                    isConnecting = false;
                }
            }
            catch
            {
                isConnecting = false;
                throw;
            }
            finally
            {
                isConnecting = false; //保险一点
            }
        }
        private async Task OnClose()
        {
            if (!_isRunning)
            {
                return;
            }

            _fetcher.Errors.TryAdd(ErrorCodes.CLIENT_DISCONNECTED, $"OpenLive 弹幕客户端已断开连接");

            _isRunning = false;
            Clear();

            _fetcher.Log($"[OpenLive] 连接断开, 将重新连接");
            await TryConnect();
        }

        public void Dispose()
        {
            _isDisposed = true;
            Clear();
        }
        private void Clear()
        {
            _chatClient?.Dispose();
            _chatClient = null;
            _timer?.Dispose();
            _timer = null;
        }
        bool isTryConnecting = false;
        async Task TryConnect()
        {
            if (isTryConnecting)
            {
                return;
            }
            isTryConnecting = true;
            while (!_isDisposed)
            {
                try
                {
                    await Init();
                    await Connect();
                    break;
                }
                catch (Exception ex)
                {
                    Dispose();
                    _fetcher.Log($"[OpenLive] 无法重新连接, 10秒后重试: {ex.Message}");
                    Thread.Sleep(10000);
                }
            }
            isTryConnecting = false;
        }

        private void WebSocketBLiveClient_ReceiveNotice(string raw, System.Text.Json.Nodes.JsonNode jObject)
        {
            _fetcher.AddEvent(raw);
        }
        private async Task<AppStartData> StartRoomAsync(string token)
        {
            try
            {
                var response = await Utils.GetAsync($"{_fetcher.VTSURU_API_URL}open-live/start?token={token}");
                var res = JObject.Parse(response);

                if ((int)res["code"] == 200)
                {
                    _fetcher.Errors.Remove(ErrorCodes.OPEN_LIVE_UNABLE_START_GAME);
                    return JsonConvert.DeserializeObject<AppStartData>(res["data"].ToString());
                }
                else
                {
                    _fetcher.Log("[START ROOM] " + res["message"].ToString());
                    _fetcher.Errors.TryAdd(ErrorCodes.OPEN_LIVE_UNABLE_START_GAME, "[OpenLive] 无法开启场次: " + res["message"].ToString());
                }
            }
            catch (Exception err)
            {
                _fetcher.Log("[OpenLive] 无法开启场次: " + err.Message);
            }

            return null;
        }

        async Task<bool> SendHeartbeatAsync()
        {
            if (_chatClient == null || _authInfo == null)
                return false;

            try
            {
                var response = await Utils.GetAsync(_fetcher.VTSURU_API_URL + "open-live/heartbeat-internal?token=" + _fetcher.VTsuruToken);

                dynamic resp = JObject.Parse(response);

                if (resp.code != 200)
                {
                    _fetcher.Log($"[HEARTBEAT] 直播场认证信息已过期: {resp.message}");
                    RestartRoom();
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }

        }
        void RestartRoom()
        {
            _chatClient?.Dispose();
            _chatClient = null;
            _ = TryConnect();
        }
    }
}
