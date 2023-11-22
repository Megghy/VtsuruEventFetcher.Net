using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenBLive.Client.Data;
using OpenBLive.Runtime;
using OpenBLive.Runtime.Data;

namespace VtsuruEventFetcher.Net
{
    public static partial class EventFetcher
    {
        public enum EventDataTypes
        {
            Guard,
            SC,
            Gift,
            Message
        }
        public class EventModel
        {
            [JsonProperty("type")]
            public EventDataTypes Type { get; set; }
            [StringLength(20)]
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("uid")]
            public long UId { get; set; }
            [StringLength(50)]
            [JsonProperty("msg")]
            public string Msg { get; set; }
            [JsonProperty("time")]
            public long Time { get; set; }
            [JsonProperty("num")]
            public int Num { get; set; }
            [JsonProperty("price")]
            public decimal Price { get; set; }
            [JsonProperty("guard_level")]
            public int GuardLevel { get; set; }
            [JsonProperty("fans_medal_level")]
            public int FansMedalLevel { get; set; }
            [JsonProperty("fans_medal_name")]
            public string FansMedalName { get; set; }
            [JsonProperty("fans_medal_wearing_status")]
            public bool FansMedalWearingStatus { get; set; }
        }
        static string VTSURU_TOKEN;
        static System.Timers.Timer _timer;
        static HttpClient client = new(new HttpClientHandler()
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {

        };
        const string VTSURU_BASE_URL = "https://hongkong.vtsuru.live/api/";
        const string VTSURU_EVENT_URL = VTSURU_BASE_URL + "event/";

        static List<EventModel> events = new();
        static string status = "ok";
        static string code = "";
        static AppStartData authInfo;
        static WebSocketBLiveClient chatClient;
        static ILogger<VTsuruEventFetcherWorker> _logger;

        public static void Init(ILogger<VTsuruEventFetcherWorker> logger)
        {
            _logger = logger;
            VTSURU_TOKEN = Environment.GetEnvironmentVariable("VTSURU_TOKEN") ?? "";
            if (string.IsNullOrEmpty(VTSURU_TOKEN))
            {
                var tokenPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "token.txt");
                if (File.Exists(tokenPath))
                {
                    VTSURU_TOKEN = File.ReadAllText(tokenPath).Trim();
                }
            }

            if (string.IsNullOrEmpty(VTSURU_TOKEN))
            {
                _logger.LogError($"未提供 VTSURU_TOKEN 变量");
                Environment.Exit(0);
            }

            Log("token: " + VTSURU_TOKEN);

            // Starting the Heartbeat
            _timer = new()
            {
                Interval = 20 * 1000,
                AutoReset = true
            };
            _timer.Elapsed += (_, _) => SendHeartbeat();
            _timer.Start();

            SendEvent();

            var port = Environment.GetEnvironmentVariable("PORT");
        }
        static void SendNotice()
        {
            // 创建通知内容
            /*var content = new ToastContentBuilder()
                .AddText("你的标题")
                .AddText("你的通知文本")
                .show();*/
        }
        public static string LogPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "logs");
        static void Log(string msg)
        {
            try
            {
                if (!Directory.Exists(LogPath))
                {
                    Directory.CreateDirectory(LogPath);
                }
                var path = Path.Combine(LogPath, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} - {msg}" + Environment.NewLine);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} - {msg}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, ex);
            }
        }
        static async Task<bool> SendHeartbeat()
        {
            if (chatClient == null || authInfo == null)
                return false;

            try
            {
                var response = await client.GetAsync(VTSURU_BASE_URL + "open-live/heartbeat-internal?token=" + VTSURU_TOKEN);

                if (!response.IsSuccessStatusCode)
                    return false;

                string responseBody = await response.Content.ReadAsStringAsync();
                dynamic resp = JObject.Parse(responseBody);

                if (resp.code != 200)
                {
                    Log($"[HEARTBEAT] 直播场认证信息已过期 {resp.message}");
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
        public static async Task<bool> GetSelfInfo()
        {
            try
            {
                var response = await client.GetAsync($"{VTSURU_BASE_URL}account/self?token={VTSURU_TOKEN}");
                var responseContent = await response.Content.ReadAsStringAsync();
                var res = JObject.Parse(responseContent);

                if ((int)res["code"] == 200)
                {
                    if (res["data"]["biliAuthCode"] == null)
                    {
                        Log("[GET INFO] 你尚未绑定B站账号并填写身份码, 请前往控制面板进行绑定");
                        status = "未绑定 Bilibili 账号";
                        return false;
                    }
                    // Assuming "self" is a variable where you want to store the data.
                    // You might want to parse res["data"] into an appropriate object.
                    return true;
                }
                else
                {
                    Log("[GET USER INFO] " + res["message"].ToString());
                }
            }
            catch (Exception err)
            {
                Log(err.Message);
            }

            return false;
        }
        public static async Task<AppStartData> StartRoom()
        {
            try
            {
                var response = await client.GetAsync($"{VTSURU_BASE_URL}open-live/start?token={VTSURU_TOKEN}");
                var responseContent = await response.Content.ReadAsStringAsync();
                var res = JObject.Parse(responseContent);

                if ((int)res["code"] == 200)
                {
                    return JsonConvert.DeserializeObject<AppStartData>(res["data"].ToString());
                }
                else
                {
                    Log("[START ROOM] " + res["message"].ToString());
                }
            }
            catch (Exception err)
            {
                Log(err.Message);
            }

            return null;
        }
        static bool isFirst = true;

        [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
        [RequiresDynamicCode("")]
        public static async Task<bool> SendEvent()
        {
            var tempEvents = events.Take(20).ToList();
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(tempEvents), Encoding.UTF8, "application/json");
                var response = await client.PostAsync($"{VTSURU_EVENT_URL}update?token={VTSURU_TOKEN}&status={status}", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                var res = JObject.Parse(responseContent);

                if ((int)res["code"] == 200)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    if (tempEvents.Count > 0)
                    {
                        Log($"[ADD EVENT] 已发送 {tempEvents.Count} 条事件: " +
                            $"舰长: {tempEvents.Count(e => e.Type == EventDataTypes.Guard)}, " +
                            $"SC: {tempEvents.Count(e => e.Type == EventDataTypes.SC)}, " +
                            $"礼物: {tempEvents.Count(e => e.Type == EventDataTypes.Gift)}, " +
                            $"弹幕: {tempEvents.Count(e => e.Type == EventDataTypes.Message)}");
                        events.RemoveRange(0, tempEvents.Count);
                    }
                    if (!string.IsNullOrEmpty(code) && code != res["data"].ToString())
                    {
                        Log("[ADD EVENT] 房间号改变, 重新连接");
                        code = res["data"].ToString();
                        RestartRoom();
                    }

                    code = res["data"].ToString();

                    status = "ok";

                    if (chatClient == null)
                    {
                        InitChatClient();
                    }

                    return true;
                }
                else
                {
                    Log($"[ADD EVENT] 失败: {res["message"]}");
                    return false;
                }
            }
            catch (Exception err)
            {
                Log("[ADD EVENT] 无法访问后端: " + err.Message);
                return false;
            }
            finally
            {
                await Task.Delay(1100); // Wait for 1.1 seconds before calling SendEvent again.
                await SendEvent();
            }
        }
        static bool isIniting = false;
        async static Task InitChatClient()
        {
            if (isIniting)
            {
                return;
            }
            isIniting = true;
            try
            {
                while (!(await GetSelfInfo()))
                {
                    Log("无法获取用户信息, 10秒后重试");
                    status = "无法获取用户信息";
                    await Task.Delay(10000);
                }
                authInfo = await StartRoom();
                while (authInfo == null)
                {
                    Log("无法开启场次, 10秒后重试");
                    status = "无法开启场次";
                    await Task.Delay(10000);
                    authInfo ??= await StartRoom();
                }
                //创建websocket客户端
                var WebSocketBLiveClient = new WebSocketBLiveClient(authInfo.WebsocketInfo.WssLink, authInfo.WebsocketInfo.AuthBody);
                WebSocketBLiveClient.OnDanmaku += WebSocketBLiveClientOnDanmaku;
                WebSocketBLiveClient.OnGift += WebSocketBLiveClientOnGift;
                WebSocketBLiveClient.OnGuardBuy += WebSocketBLiveClientOnGuardBuy;
                WebSocketBLiveClient.OnSuperChat += WebSocketBLiveClientOnSuperChat;
                //连接长链  需自己处理重连
                //m_WebSocketBLiveClient.Connect();
                //连接长链 带有自动重连
                WebSocketBLiveClient.Close += (_, _) => RestartRoom();
                var success = await WebSocketBLiveClient.Connect(TimeSpan.FromSeconds(30));
                if (!success)
                {
                    throw new("无法连接至房间");
                }
                else
                {
                    Log($"已连接直播间: {authInfo.AnchorInfo.UName}<{authInfo.AnchorInfo.Uid}>");
                    chatClient = WebSocketBLiveClient;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }

        }
        static void RestartRoom()
        {
            chatClient?.Dispose();
            chatClient = null;
            _ = InitChatClient();
        }
        //绑定醒目留言
        private static void WebSocketBLiveClientOnSuperChat(SuperChat data)
        {
            Log($"[SC事件] {data.userName}<{data.rmb}>: {data.message}");
            events.Add(new()
            {
                Type = EventDataTypes.SC,
                Name = data.userName,
                UId = data.uid,
                Msg = data.message,
                Price = data.rmb,
                Num = 1,
                Time = data.timeStamp,
                GuardLevel = (int)data.guardLevel,
                FansMedalLevel = (int)data.fansMedalLevel,
                FansMedalName = data.fansMedalName,
                FansMedalWearingStatus = data.fansMedalWearingStatus,
            });
        }
        //绑定大航海信息
        private static void WebSocketBLiveClientOnGuardBuy(Guard data)
        {
            var model = new EventModel()
            {
                Type = EventDataTypes.Guard,
                Name = data.userInfo.userName,
                UId = data.userInfo.uid,
                Msg = data.guardLevel == 1 ? "总督" : data.guardLevel == 2 ? "提督" : "舰长",
                Price = 0,
                Num = (int)data.guardNum,
                Time = data.timestamp,
                GuardLevel = (int)data.guardLevel,
                FansMedalLevel = (int)data.fansMedalLevel,
                FansMedalName = data.fansMedalName,
                FansMedalWearingStatus = data.fansMedalWearingStatus,
            };
            events.Add(model);

            Log($"[上舰事件] {data.userInfo.userName}: <{model.Msg}> {data.guardNum}{data.guardUnit}");
        }
        //绑定礼物信息
        private static void WebSocketBLiveClientOnGift(SendGift data)
        {
            Log($"[礼物事件] {data.userName}: {data.giftName}<{data.giftNum}个> ¥{((double)data.price * data.giftNum) / 1000:0.0}");
            events.Add(new()
            {
                Type = EventDataTypes.Gift,
                Name = data.userName,
                UId = data.uid,
                Msg = data.giftName,
                Price = (decimal)(((double)data.price * data.giftNum) / 1000),
                Num = (int)data.giftNum,
                Time = data.timestamp,
                GuardLevel = (int)data.guardLevel,
                FansMedalLevel = (int)data.fansMedalLevel,
                FansMedalName = data.fansMedalName,
                FansMedalWearingStatus = data.fansMedalWearingStatus,
            });
        }
        //绑定弹幕事件
        private static void WebSocketBLiveClientOnDanmaku(Dm data)
        {
            Log($"[弹幕事件] {data.userName}: {data.msg}");
            events.Add(new()
            {
                Type = EventDataTypes.Message,
                Name = data.userName,
                UId = data.uid,
                Msg = data.msg,
                Price = 0,
                Num = 1,
                Time = data.timestamp,
                GuardLevel = (int)data.guardLevel,
                FansMedalLevel = (int)data.fansMedalLevel,
                FansMedalName = data.fansMedalName,
                FansMedalWearingStatus = data.fansMedalWearingStatus,
            });
        }
    }
}
