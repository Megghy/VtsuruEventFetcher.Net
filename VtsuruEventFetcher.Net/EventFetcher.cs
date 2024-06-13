using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using MessagePack;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VtsuruEventFetcher.Net.DanmakuClient;

namespace VtsuruEventFetcher.Net
{
    public static class ErrorCodes
    {
        public const string ACCOUNT_NOT_BIND = "Account.NotBind";
        public const string ACCOUNT_UNABLE_GET_INFO = "Account.UnableGet";

        public const string OPEN_LIVE_UNABLE_START_GAME = "OpenLive.UnableStart";

        public const string COOKIE_CLIENT_UNABLE_GET_COOKIE = "CookieClient.GetCookie";
        public const string COOKIE_CLIENT_UNABLE_GET_USER_INFO = "CookieClient.UnableGetInfo";

        public const string NEW_VERSION = "NewVersion";

        public const string CLIENT_DISCONNECTED = "Client.Disconnected";
        public const string UNABLE_UPLOAD_EVENT = "UnableUploadEvent";
        public const string UNABLE_CONNECTTOHUB = "UnableConnectToHub";
    }
    public static partial class EventFetcher
    {
        static void Log(string msg)
            => Utils.Log(msg);

        public static string VTSURU_TOKEN { get; private set; }
        public static string? COOKIE_CLOUD_KEY { get; private set; }
        public static string? COOKIE_CLOUD_PASSWORD { get; private set; }
        public static string? COOKIE_CLOUD_HOST { get; private set; }
        public static string? BILI_COOKIE { get; private set; }

        public const string VTSURU_DEFAULT_URL = "https://vtsuru.suki.club/";
        public const string VTSURU_FAILOVER_URL = "https://failover-api.vtsuru.suki.club/";

        public static readonly bool IsDockerEnv = File.Exists("/.dockerenv")
                                  || IsDockerCGroupPresent()
                                  || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

        static bool IsDockerCGroupPresent()
        {
            try
            {
                string[] lines = File.ReadAllLines("/proc/self/cgroup");
                foreach (var line in lines)
                {
                    if (line.Contains("docker") || line.Contains("kubepods"))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Handle exceptions if needed
            }

            return false;
        }

        private static int failUseCount = 0;
        public static string VTSURU_URL
        {
            get
            {
                if (failUseCount > 0)
                {
                    failUseCount--;
                    if (failUseCount == 0)
                    {
                        Log($"[VTSURU] 将再次尝试连接主服务器");
                    }
                    return VTSURU_FAILOVER_URL;
                }
                else
                {
                    return VTSURU_DEFAULT_URL;
                }
            }
        }
        private static void OnFail()
        {
            if (failUseCount <= 0)
            {
                Log("[VTSURU] 无法连接到 VTSURU, 切换至备用服务器");
            }
            failUseCount = 3;
        }

        public static string VTSURU_API_URL => VTSURU_URL + "api/";
        public static string VTSURU_HUB_URL => VTSURU_URL + "hub/";
        public static string VTSURU_EVENT_URL => VTSURU_API_URL + "event/";

        private static readonly string _osInfo = Environment.OSVersion.ToString();
        private static readonly string _version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public static readonly string User_Agent = $"VTsuruEventFetcher/{_version} ({_osInfo}) {(accountInfo is null ? "" : $"{accountInfo["data"]?["name"]}/{uId}")}";

        internal static System.Timers.Timer _timer;
        internal static List<string> _events = [];
        internal static Dictionary<string, string> Errors = [];
        internal static ILogger<VTsuruEventFetcherWorker> _logger;
        internal static HubConnection _hub;
        internal static IDanmakuClient _client;

        internal static DateTime lastUploadEvent = DateTime.MinValue;
        internal static TimeSpan uploadIntervalWhenEmpty = TimeSpan.FromMinutes(1);
        internal static JToken accountInfo;
        internal static long uId;
        internal static long roomId;
        internal static string code;

        private static DateTime _lastCheckLogTime = DateTime.Now;

        public static bool UsingCookie
            => !string.IsNullOrEmpty(BILI_COOKIE) || (!string.IsNullOrEmpty(COOKIE_CLOUD_KEY) && !string.IsNullOrEmpty(COOKIE_CLOUD_PASSWORD));

        public static void Init(ILogger<VTsuruEventFetcherWorker> logger)
        {
            _logger = logger;

            var tokenPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "token.txt");
            var configPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "config.json");
            if (File.Exists(tokenPath))
            {
                var text = File.ReadAllText(tokenPath).Trim();
                var config = new Config()
                {
                    Token = text
                };
                File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                File.Delete(tokenPath);
                Log($"尝试将token文件转换为json配置文件");
            }
            if (File.Exists(configPath))
            {
                try
                {
                    var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                    VTSURU_TOKEN = config.Token;
                    COOKIE_CLOUD_KEY = config.CookieCloudKey;
                    COOKIE_CLOUD_PASSWORD = config.CookieCloudPassword;
                    COOKIE_CLOUD_HOST = config.CookieCloudHost;
                    BILI_COOKIE = config.Cookie;
                }
                catch (Exception ex)
                {
                    Log($"读取配置文件失败: {ex}");
                    Environment.Exit(0);
                }
            }

            if (Environment.GetEnvironmentVariable("VTSURU_TOKEN")?.Trim() is { Length: > 0 } token)
            {
                VTSURU_TOKEN = token;
            }
            if (Environment.GetEnvironmentVariable("BILI_COOKIE")?.Trim() is { Length: > 0 } cookie)
            {
                BILI_COOKIE = cookie;
            }

            if (Environment.GetEnvironmentVariable("COOKIE_CLOUD")?.Trim() is { Length: > 0 } cookieCloud)
            {
                if (!cookieCloud.Contains('@'))
                {
                    _logger.LogError($"无效的 CookieCloud 秘钥, 应为 KEY@PASSWORD (用户key + @ + 端到端加密密码)");
                    Environment.Exit(0);
                }
                else
                {
                    COOKIE_CLOUD_KEY = cookieCloud.Split('@')[0];
                    COOKIE_CLOUD_PASSWORD = cookieCloud.Split('@')[1];
                }
            }

            if (!string.IsNullOrEmpty(COOKIE_CLOUD_KEY) && !string.IsNullOrEmpty(COOKIE_CLOUD_PASSWORD))
            {
                Log("已设置 CookieCloud, 将从云端获取 Cookie");
            }
            else if (!string.IsNullOrEmpty(BILI_COOKIE))
            {
                Log("已设置逸站 Cookie, 将使用你的账户连接直播间");
            }
            else
            {
                Log("未设置 Cookie 或 CookieCloud, 将使用开放平台进行连接");
            }

            COOKIE_CLOUD_HOST ??= Environment.GetEnvironmentVariable("COOKIE_CLOUD_HOST")?.Trim();
            if (!string.IsNullOrEmpty(COOKIE_CLOUD_HOST))
            {
                try
                {
                    var u = new Uri(COOKIE_CLOUD_HOST);
                    Log($"已设置 CookieCloud 自定义域名为: {COOKIE_CLOUD_HOST}");
                }
                catch
                {
                    _logger.LogError($"无效的自定义 CookieCloud Host");
                    Environment.Exit(0);
                }
            }

            if (string.IsNullOrEmpty(VTSURU_TOKEN))
            {
                Log($"未提供 Token");
                Environment.Exit(0);
            }

            Log("VTsuru Token: " + VTSURU_TOKEN);

            _ = InitChatClientAsync();
            _ = ConnectHub();
            var timer = new System.Timers.Timer()
            {
                AutoReset = true,
                Interval = 1000,
            };
            timer.Elapsed += (e, args) =>
            {
                if(_lastCheckLogTime < DateTime.Now - TimeSpan.FromMinutes(1))
                {
                    _lastCheckLogTime = DateTime.Now;
                    Utils.ClearLog();
                }
                if(_events.Count > 0 || DateTime.Now - lastUploadEvent > uploadIntervalWhenEmpty)
                {
                    _ = SendEventAsync();
                    lastUploadEvent = DateTime.Now;
                }
            };
            timer.Start();

            var port = Environment.GetEnvironmentVariable("PORT");
            if (!string.IsNullOrEmpty(port))
            {
                _ = Task.Run(async () =>
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://*:{port}/");
                    listener.Start();
                    Console.WriteLine($"正在监听端口以通过健康监测: {port}");

                    while (true)
                    {
                        var context = await listener.GetContextAsync();
                        var response = context.Response;
                        var responseString = "VTsuruEventFetcher";
                        var buffer = Encoding.UTF8.GetBytes(responseString);
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer);
                        response.OutputStream.Close();
                    }
                });
            }
        }
        static void SendNotice()
        {
            // 创建通知内容
            /*var content = new ToastContentBuilder()
                .AddText("你的标题")
                .AddText("你的通知文本")
                .show();*/
        }
        public static async Task<bool> GetSelfInfoAsync()
        {
            try
            {
                var response = await Utils.GetAsync($"{VTSURU_API_URL}account/self?token={VTSURU_TOKEN}");
                var res = JObject.Parse(response);

                if ((int)res["code"] == 200)
                {
                    if (res["data"]["biliAuthCode"] == null)
                    {
                        Log("[GET INFO] 你尚未绑定B站账号并填写身份码, 请前往控制面板进行绑定");
                        Errors.TryAdd(ErrorCodes.ACCOUNT_NOT_BIND, "你尚未绑定B站账号并填写身份码, 请前往控制面板进行绑定");
                        return false;
                    }
                    else
                    {
                        Errors.Remove(ErrorCodes.ACCOUNT_NOT_BIND);
                    }
                    // Assuming "self" is a variable where you want to store the data.
                    // You might want to parse res["data"] into an appropriate object.
                    accountInfo = res["data"];
                    uId = (long)res["data"]["biliId"];
                    roomId = (long)res["data"]["biliRoomId"];
                    return true;
                }
                else
                {
                    Log("[GET USER INFO] " + res["message"].ToString());
                }
            }
            catch (Exception err)
            {
                OnFail();
                Log(err.Message);
            }

            return false;
        }
        static bool isFirst = true;
        static bool isDisconnectByServer = false;
        static readonly Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        record SendEventModel(IEnumerable<string> Events, Dictionary<string, string> Error, Version CurrentVersion);
        public record ResponseEventModelV3(string Code, Version DotnetVersion, int EventCount);

        static long _addEventErrorCount = 0;
        static bool isConnectingHub = false;
        private static async Task ConnectHub()
        {
            if (isConnectingHub)
            {
                return;
            }
            isConnectingHub = true;
            var connection = new HubConnectionBuilder()
                .WithUrl(VTSURU_HUB_URL + $"event-fetcher?token={VTSURU_TOKEN}")
                .WithAutomaticReconnect()
                .AddMessagePackProtocol()
                .Build();

            connection.Closed += (error) => _ = OnHubClosed(error);
            connection.On("Disconnect", async (string e) =>
            {
                isDisconnectByServer = true;
                Log($"被服务端断开连接: {e}, 为保证可用性将于30s后再次尝试连接");
                await Task.Delay(30000);
                _ = ConnectHub();
            });

            while (true)
            {
                try
                {
                    await connection.StartAsync();
                    _hub = connection;
                    isDisconnectByServer = false;
                    Log($"已连接至 VTsuru 服务器");

                    break;
                }
                catch (Exception ex)
                {
                    Log(ex.ToString());
                    Errors.TryAdd(ErrorCodes.UNABLE_CONNECTTOHUB, $"无法连接至 VTsuru 服务器");
                }
            }
            isConnectingHub = false;
            Errors.Remove(ErrorCodes.UNABLE_CONNECTTOHUB);
        }
        async static Task OnHubClosed(Exception error)
        {
            if (isDisconnectByServer || error is null)
            {
                return;
            }
            _hub = null;
            Log($"与服务器的连接断开, 正在重连: {error?.Message}");
            OnFail();
            await Task.Delay(new Random().Next(0, 5) * 1000);
            await ConnectHub();
        }
        static bool isUploading = false;
        [MessagePackObject(keyAsPropertyName: true)]
        public record RequestUploadEvents(string[] Events, Dictionary<string, string> Error, Version? CurrentVersion, string OSInfo, bool UseCookie);
        [MessagePackObject(keyAsPropertyName: true)]
        public record ResponseUploadEvents(bool Success, string Message, Version Version, int EventCount);
        public static async Task<bool> SendEventAsync()
        {
            if (_hub is null || _hub.State is not HubConnectionState.Connected || isUploading)
            {
                return false;
            }
            isUploading = true;
            try
            {
                var tempEvents = _events.Take(30).ToArray();
                var model = new RequestUploadEvents(tempEvents, Errors, currentVersion, _osInfo, UsingCookie);

                var messagePackData = MessagePackSerializer.Serialize(model);

                // 使用Brotli进行压缩
                using var compressedData = new MemoryStream();
                using var compressor = new BrotliStream(compressedData, CompressionMode.Compress);
                await compressor.WriteAsync(messagePackData);
                await compressor.FlushAsync();
                var data = compressedData.ToArray();

                var resp = await _hub?.InvokeAsync<ResponseUploadEvents>("UploadEvents", data);

                if (resp.Success)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    if (tempEvents.Length > 0)
                    {
                        Log($"[ADD EVENT] 已发送 {tempEvents.Length} 条事件");
                        _events.RemoveRange(0, tempEvents.Length);
                    }
                    var responseCode = resp.Message;
                    if (!string.IsNullOrEmpty(code) && code != responseCode)
                    {
                        Log("[ADD EVENT] 房间号改变, 重新连接");
                        code = responseCode;
                        RestartRoom();
                    }
                    else
                    {
                        var version = resp.Version;
                        if (version > currentVersion)
                        {
                            Errors.TryAdd(ErrorCodes.NEW_VERSION, "发现新版本: " + version);
                        }
                        else
                        {
                            Errors.Remove(ErrorCodes.NEW_VERSION);
                        }

                        code = responseCode;

                        if (_client is null)
                        {
                            _ = InitChatClientAsync();
                        }
                    }
                    _addEventErrorCount = 0;

                    return true;
                }
                else
                {
                    Log($"[ADD EVENT] 失败: {resp.Message}");
                    return false;
                }
            }
            catch (Exception err)
            {
                Log("[ADD EVENT] 上传事件失败: " + err.Message);
                _addEventErrorCount++;
                return false;
            }
            finally
            {
                if (_addEventErrorCount > 5)
                {
                    Errors.TryAdd(ErrorCodes.UNABLE_UPLOAD_EVENT, "无法发送事件, 请检查网络情况");
                }
                else
                {
                    Errors.Remove(ErrorCodes.UNABLE_UPLOAD_EVENT);
                }
                isUploading = false;
            }
        }
        static bool isIniting = false;
        async static Task InitChatClientAsync()
        {
            if (isIniting)
            {
                return;
            }
            isIniting = true;
            try
            {
                while (!(await GetSelfInfoAsync()))
                {
                    Log("无法获取用户信息, 10秒后重试");
                    Errors.TryAdd(ErrorCodes.ACCOUNT_UNABLE_GET_INFO, "无法获取用户信息");
                    await Task.Delay(10000);
                }

                Errors.Remove(ErrorCodes.ACCOUNT_UNABLE_GET_INFO);

                if (UsingCookie)
                {
                    _client = new CookieClient(BILI_COOKIE, COOKIE_CLOUD_KEY, COOKIE_CLOUD_PASSWORD, COOKIE_CLOUD_HOST);
                }
                else
                {
                    _client = new OpenLiveClient();
                }
                while (true)
                {
                    try
                    {
                        await _client.Init();
                        await _client.Connect();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Utils.Log($"无法启动弹幕客户端, 10秒后重试: {ex.Message}");
                        Thread.Sleep(10000);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
            finally
            {
                isIniting = false;
            }

        }
        static void RestartRoom()
        {
            _client?.Dispose();

            _ = InitChatClientAsync();
        }
        public static void AddEvent(string e)
        {
            _events.Add(e);
        }
    }
}
