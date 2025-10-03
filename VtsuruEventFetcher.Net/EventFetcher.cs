using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Linq;
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
    public class EventFetcher
    {
        private string _labelCache;

        internal void Log(string msg)
            => Utils.Log($"[{GetInstanceLabel()}] {msg}");

        public string VTsuruToken { get; private set; }
        public static string? COOKIE_CLOUD_KEY { get; internal set; }
        public static string? COOKIE_CLOUD_PASSWORD { get; internal set; }
        public static string? COOKIE_CLOUD_HOST { get; internal set; }
        public static string? BILI_COOKIE { get; internal set; }

        public const string VTSURU_DEFAULT_URL = "https://api.vtsuru.suki.club/";
        public const string VTSURU_FAILOVER_URL = "https://failover-api.vtsuru.suki.club/";


        private int failUseCount = 0;
        public string VTSURU_URL
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
        private void OnFail()
        {
            if (failUseCount <= 0)
            {
                Log("[VTSURU] 无法连接到 VTSURU, 切换至备用服务器");
            }
            failUseCount = 3;
        }

        public string VTSURU_API_URL => VTSURU_URL + "api/";
        public string VTSURU_HUB_URL => VTSURU_URL + "hub/";
        public string VTSURU_EVENT_URL => VTSURU_API_URL + "event/";

        private static readonly string _osInfo = Environment.OSVersion.ToString();
        private static readonly string _version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        internal System.Timers.Timer _timer;
        internal List<string> _events = [];
        internal Dictionary<string, string> Errors = [];
        internal ILogger<VTsuruEventFetcherWorker> _logger;
        internal HubConnection _hub;
        internal IDanmakuClient _client;

        internal DateTime lastUploadEvent = DateTime.MinValue;
        internal TimeSpan uploadIntervalWhenEmpty = TimeSpan.FromMinutes(1);
        internal JToken accountInfo;
        internal long uId;
        internal long roomId;
        internal string code;

        private DateTime _lastCheckLogTime = DateTime.Now;

        public bool UsingCookie
            => !string.IsNullOrEmpty(BILI_COOKIE) || (!string.IsNullOrEmpty(COOKIE_CLOUD_KEY) && !string.IsNullOrEmpty(COOKIE_CLOUD_PASSWORD));

        public void Init(ILogger<VTsuruEventFetcherWorker> logger, string? token)
        {
            _logger = logger;
            VTsuruToken = token.Trim();
            _labelCache = null;

            

            if (string.IsNullOrEmpty(VTsuruToken))
            {
                Log($"未提供 Token");
                throw new InvalidOperationException("未提供 Token");
            }
            if (!GetSelfInfoAsync().Result)
            {
                Log("提供的 Token 无效");
                throw new InvalidOperationException("提供的 Token 无效");
            }

            Log("VTsuru Token: " + MaskToken(token));

            _ = ConnectHub();

            _timer = new System.Timers.Timer()
            {
                AutoReset = true,
                Interval = 1000,
            };
            _timer.Elapsed += (e, args) =>
            {
                if (_lastCheckLogTime < DateTime.Now - TimeSpan.FromMinutes(1))
                {
                    _lastCheckLogTime = DateTime.Now;
                    Utils.ClearLog();
                }
                if (_events.Count > 0 || DateTime.Now - lastUploadEvent > uploadIntervalWhenEmpty)
                {
                    _ = SendEventAsync();
                    lastUploadEvent = DateTime.Now;
                }
            };
            _timer.Start();

            // 健康检查端口监听改由 Worker 统一管理，避免多实例端口冲突
        }
        public async Task<bool> GetSelfInfoAsync()
        {
            try
            {
                var response = await Utils.GetAsync($"{VTSURU_API_URL}account/self?token={VTsuruToken}");
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
        bool isFirst = true;
        bool isDisconnectByServer = false;
        static readonly Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        record SendEventModel(IEnumerable<string> Events, Dictionary<string, string> Error, Version CurrentVersion);
        public record ResponseEventModelV3(string Code, Version DotnetVersion, int EventCount);

        long _addEventErrorCount = 0;
        bool isConnectingHub = false;
        private async Task ConnectHub()
        {
            if (isConnectingHub)
            {
                return;
            }
            isConnectingHub = true;
            HubConnection connection = null;
            CreateConnection();

            while (true)
            {
                try
                {
                    await connection.StartAsync();
                    _hub = connection;
                    isDisconnectByServer = false;
                    Log($"已连接至 VTsuru 服务器");

                    await _hub.SendAsync("ConnectFinished", _version, UsingCookie);

                    _ = InitChatClientAsync();

                    break;
                }
                catch (Exception ex)
                {
                    Log(ex.ToString());
                    Errors.TryAdd(ErrorCodes.UNABLE_CONNECTTOHUB, $"无法连接至 VTsuru 服务器: {ex.Message}");
                    await Task.Delay(5000);
                    CreateConnection();
                }
            }
            void CreateConnection()
            {
                connection = new HubConnectionBuilder()
                .WithUrl(VTSURU_HUB_URL + $"event-fetcher?token={VTsuruToken}")
                .WithAutomaticReconnect()
                .AddMessagePackProtocol()
                .Build();

                connection.Closed += (error) => _ = OnHubClosed(error);
                connection.On("Disconnect", async (string e) =>
                {
                    isDisconnectByServer = true;
                    _ = connection?.DisposeAsync();
                    if (_hub == connection)
                    {
                        _hub = null;
                    }
                    Log($"被服务端断开连接: {e}, 为保证可用性将于30s后再次尝试连接");

                    _client?.Dispose();

                    await Task.Delay(30000);
                    _ = ConnectHub();
                });
                connection.On("Request", async (string url, string method, string body, bool useCookie) =>
                {
                    return await RequestAsync(url, method, body, useCookie);
                });
            }
            isConnectingHub = false;
            Errors.Remove(ErrorCodes.UNABLE_CONNECTTOHUB);
        }
        async Task OnHubClosed(Exception error)
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
        bool isUploading = false;
        [MessagePackObject(keyAsPropertyName: true)]
        public record RequestUploadEvents(string[] Events, Dictionary<string, string> Error, Version? CurrentVersion, string OSInfo, bool UseCookie);
        [MessagePackObject(keyAsPropertyName: true)]
        public record ResponseUploadEvents(bool Success, string Message, Version Version, int EventCount);
        public async Task<bool> SendEventAsync()
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
        [MessagePackObject(keyAsPropertyName: true)]
        public record ResponseClientRequestData(bool Success, string Message, string Data);
        /// <summary>
        /// 用于获取粉丝, 舰长等数据, 如果不放心的话可以自行添加url代码检查
        /// </summary>
        /// <param name="url">仅用于bilibili域名下</param>
        /// <param name="method"></param>
        /// <param name="body"></param>
        /// <param name="useCookie"></param>
        /// <returns></returns>
        public async Task<ResponseClientRequestData> RequestAsync(string url, string method = "GET", string body = null, bool useCookie = true)
        {
            using var client = new HttpClient();
            try
            {
                var uri = new Uri(url);
                if (!uri.Host.ToLower().EndsWith("bilibili.com"))
                {
                    Log($"[Request] 请求失败: 非bilibili域名");
                    return new(false, $"请求失败: 非bilibili域名", string.Empty);
                }
                var request = new HttpRequestMessage(new HttpMethod(method), url);
                var content = string.IsNullOrEmpty(body) ? null : new StringContent(body, Encoding.UTF8, "application/json");
                request.Content = content;

                if (useCookie && !UsingCookie)
                {
                    return new(false, "未启用cookie", string.Empty);
                }
                if (useCookie && UsingCookie)
                {
                    request.Headers.TryAddWithoutValidation("Cookie", (_client as CookieClient)._cookie);
                }

                var response = await client.SendAsync(request);
                Log($"[Request] 请求URL: {url}, 请求方法: {method}, 响应: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    Log($"[Request] 请求失败: {response.StatusCode}");
                    return new(false, $"请求失败: {response.StatusCode}, {response.ReasonPhrase}", string.Empty);
                }
                var result = await response.Content.ReadAsStringAsync();
                return new(true, string.Empty, result);
            }
            catch (Exception ex)
            {
                Log($"[Request] 请求失败: {ex.Message}");
                return new(false, $"请求失败: {ex.Message}", string.Empty);
            }
        }
        bool isIniting = false;
        async Task InitChatClientAsync()
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
                    _client = new CookieClient(this, BILI_COOKIE, COOKIE_CLOUD_KEY, COOKIE_CLOUD_PASSWORD, COOKIE_CLOUD_HOST);
                }
                else
                {
                    _client = new OpenLiveClient(this);
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
        void RestartRoom()
        {
            _client?.Dispose();

            _ = InitChatClientAsync();
        }
        public void AddEvent(string e)
        {
            _events.Add(e);
        }

        public async Task StopAsync()
        {
            try
            {
                _timer?.Stop();
                _timer?.Dispose();
                _timer = null;
            }
            catch { }
            try
            {
                _client?.Dispose();
                _client = null;
            }
            catch { }
            try
            {
                if (_hub is not null)
                {
                    await _hub.DisposeAsync();
                    _hub = null;
                }
            }
            catch { }
        }

        public string GetInstanceLabel()
        {
            if (!string.IsNullOrWhiteSpace(_labelCache))
            {
                return _labelCache;
            }

            _labelCache = accountInfo?["name"]?.ToString() ?? $"Token:{MaskToken(VTsuruToken)}";
            return _labelCache;
        }

        public static string MaskToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "Unknown";
            }

            var suffixLength = Math.Min(4, token.Length);
            return $"{token[..suffixLength]}***";
        }
    }
}
