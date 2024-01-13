using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenBLive.Client.Data;
using OpenBLive.Runtime;

namespace VtsuruEventFetcher.Net.DanmakuClient
{
    public class OpenLiveClient : IDanmakuClient
    {
        AppStartData authInfo;
        WebSocketBLiveClient chatClient;
        System.Timers.Timer _timer;
        public OpenLiveClient()
        {

        }
        public async Task Init()
        {
            authInfo = await StartRoomAsync(EventFetcher.VTSURU_TOKEN);
            while (authInfo == null)
            {
                Utils.Log("[OpenLive] 无法开启场次, 10秒后重试");
                await Task.Delay(10000);
                authInfo ??= await StartRoomAsync(EventFetcher.VTSURU_TOKEN);
            }

            if (_timer is null)
            {
                // Starting the Heartbeat
                _timer ??= new()
                {
                    Interval = 20 * 1000,
                    AutoReset = true
                };
                _timer.Elapsed += (_, _) => _ =  SendHeartbeatAsync();
                _timer.Start();
            }
            
        }
        public async Task Connect()
        {
            //创建websocket客户端
            var WebSocketBLiveClient = new WebSocketBLiveClient(authInfo.WebsocketInfo.WssLink, authInfo.WebsocketInfo.AuthBody);
            WebSocketBLiveClient.ReceiveNotice += WebSocketBLiveClient_ReceiveNotice;
            //连接长链  需自己处理重连
            //m_WebSocketBLiveClient.Connect();
            //连接长链 带有自动重连
            WebSocketBLiveClient.Close += (_, _) => RestartRoom();
            var success = await WebSocketBLiveClient.Connect();
            if (!success)
            {
                throw new("[OpenLive] 无法连接至房间");
            }
            else
            {
                Utils.Log($"[OpenLive] 已连接直播间: {authInfo.AnchorInfo.UName}<{authInfo.AnchorInfo.Uid}>");
                chatClient = WebSocketBLiveClient;
            }
        }

        public void Dispose()
        {
            chatClient?.Dispose();
            chatClient = null;
            _timer?.Dispose();
            _timer = null;
        }

        private void WebSocketBLiveClient_ReceiveNotice(string raw, System.Text.Json.Nodes.JsonNode jObject)
        {
            EventFetcher.AddEvent(raw);
        }
        public static async Task<AppStartData> StartRoomAsync(string token)
        {
            try
            {
                var response = await Utils.client.GetAsync($"{EventFetcher.VTSURU_BASE_URL}open-live/start?token={token}");
                var responseContent = await response.Content.ReadAsStringAsync();
                var res = JObject.Parse(responseContent);

                if ((int)res["code"] == 200)
                {
                    EventFetcher.Errors.Remove(ErrorCodes.OPEN_LIVE_UNABLE_START_GAME);
                    return JsonConvert.DeserializeObject<AppStartData>(res["data"].ToString());
                }
                else
                {
                    Utils.Log("[START ROOM] " + res["message"].ToString());

                    EventFetcher.Errors.TryAdd(ErrorCodes.OPEN_LIVE_UNABLE_START_GAME, "[OpenLive] 无法开启场次: " + res["message"].ToString());
                }
            }
            catch (Exception err)
            {
                Utils.Log(err.Message);
            }

            return null;
        }

        async Task<bool> SendHeartbeatAsync()
        {
            if (chatClient == null || authInfo == null)
                return false;

            try
            {
                var response = await Utils.client.GetAsync(EventFetcher.VTSURU_BASE_URL + "open-live/heartbeat-internal?token=" + EventFetcher.VTSURU_TOKEN);

                if (!response.IsSuccessStatusCode)
                    return false;

                string responseBody = await response.Content.ReadAsStringAsync();
                dynamic resp = JObject.Parse(responseBody);

                if (resp.code != 200)
                {
                    Utils.Log($"[HEARTBEAT] 直播场认证信息已过期: {resp.message}");
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
            chatClient?.Dispose();
            chatClient = null;
            _ = Init();
            _ = Connect();
        }
    }
}
