using System.Text;
using BDanMuLib;
using Newtonsoft.Json.Linq;

namespace VtsuruEventFetcher.Net.DanmakuClient
{
    internal class CookieClient(EventFetcher fetcher, string? cookie, string? cookieCloudKey, string? cookieCloudPassword, string? cookieCloudHost) : IDanmakuClient
    {
        private readonly EventFetcher _fetcher = fetcher;
        bool _isRunning = false;
        DanMuCore _danmu;
        bool isConnecting = false;
        public async Task Connect()
        {
            if (isConnecting)
            {
                return;
            }
            isConnecting = true;
            try
            {
                var danmu = new DanMuCore(DanMuCore.ClientType.Wss, _fetcher.roomId, _fetcher.uId, _cookie, uId);
                danmu.ReceiveRawMessage += Danmu_ReceiveRawMessage;
                danmu.OnDisconnect += () => _ = Task.Run(OnClose);
                if (!await danmu.ConnectAsync())
                {
                    isConnecting = false;
                    throw new("[CookieClient] 无法连接到直播间");
                }
                _danmu = danmu;
                _isRunning = true;
                _fetcher.Errors.Remove(ErrorCodes.CLIENT_DISCONNECTED);
                _fetcher.Log($"[CookieClient] 已连接直播间: {_fetcher.roomId}");
                isConnecting = false;
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

            _isRunning = false;

            _fetcher.Errors.TryAdd(ErrorCodes.CLIENT_DISCONNECTED, $"Cookie 弹幕客户端已断开连接");

            Dispose();

            _fetcher.Log($"[CookieClient] 连接断开, 将重新连接");
            while (true)
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
                    _fetcher.Log($"[CookieClient] 无法重新连接, 10秒后重试: {ex.Message}");
                    Thread.Sleep(10000);
                }
            }
            _fetcher.Log($"[CookieClient] 已重新连接");
        }
        private bool Danmu_ReceiveRawMessage(long roomId, string msg)
        {
            if (!string.IsNullOrEmpty(msg))
            {
                _fetcher.AddEvent(msg);
            }
            return true;
        }

        public void Dispose()
        {
            _danmu?.Disconnect();
            _danmu = null;
            _updateCookieTimer?.Dispose();
            _updateCookieTimer = null;
        }


        System.Timers.Timer _updateCookieTimer;
        internal string _cookie = cookie;
        readonly string _cookieCloudKey = cookieCloudKey;
        readonly string _cookieCloudPassword = cookieCloudPassword;
        private readonly string _cookieCloudHost = string.IsNullOrEmpty(cookieCloudHost) ? "https://cookie.suki.club/" : cookieCloudHost;

        JToken userInfo;
        long uId;
        int retryCount = 0;
        public async Task Init()
        {
            while (!(await UpdateCookieAsync()))
            {
                var delayTime = (retryCount * 1000 * 10) * 2;
                if (delayTime > 10 * 60 * 1000)
                {
                    delayTime = 10 * 60 * 1000;
                }
                _fetcher.Log("[CookieClient] <第 " + retryCount + " 次> 无法从 CookieCloud 获取 Cookie, 60秒后重试");
                retryCount++;
                await Task.Delay(delayTime);
            }
            retryCount = 0;
            _fetcher.Log("[CookieClient] 已从 CookieCloud 获取 Cookie");
            while (!(await UpdateUserInfoAsync()))
            {
                _fetcher.Log("[CookieClient] 无法获取用户信息, 10秒后重试");
                await Task.Delay(10000);
            }
            if (_updateCookieTimer is null)
            {
                _updateCookieTimer = new()
                {
                    Interval = 10 * 60 * 1000,
                    AutoReset = true,
                };
                _updateCookieTimer.Elapsed += (_, _) =>
                {
                    _ = UpdateCookieAsync();
                    _ = UpdateUserInfoAsync();
                };
                _updateCookieTimer.Start();
            }
        }
        public async Task<bool> UpdateCookieAsync()
        {
            try
            {
                var uri = new Uri(new Uri(_cookieCloudHost), $"/get/{_cookieCloudKey}");
                var request = new HttpRequestMessage(HttpMethod.Post, uri);

                // 添加要发送的表单数据
                var postData = new List<KeyValuePair<string, string>>
                {
                new("password", _cookieCloudPassword),
                };

                // 创建FormUrlEncodedContent（URL编码的内容）
                request.Content = new FormUrlEncodedContent(postData);
                var result = await Utils.SendAsync(request);
                if (result.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(await result.Content.ReadAsStringAsync());
                    var biliCookie = json["cookie_data"]?["bilibili.com"];
                    if (biliCookie is not null)
                    {
                        StringBuilder cookieStringBuilder = new();

                        // Iterate through the cookie objects and build the cookie string
                        foreach (var cookie in biliCookie)
                        {
                            cookieStringBuilder.AppendFormat("{0}={1}; ", cookie["name"], cookie["value"]);
                            if (cookie["name"].Value<string>() == "DedeUserID")
                            {
                                Console.WriteLine($"Cookie 所属 Uid: {cookie["value"]}");
                            }
                        }
                        _cookie = cookieStringBuilder.ToString();
                        _fetcher.Errors.Remove(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE);
                        return true;
                    }
                    else
                    {
                        _fetcher.Log($"[CookieClient] 已从 CookieCloud 中获取数据, 但其中不存在 BiliBili 的 Cookie");
                        _fetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE, "已从 CookieCloud 中获取数据, 但其中不存在 BiliBili 的 Cookie");
                    }
                }
                else
                {
                    _fetcher.Log($"[CookieClient] 无法获取 Cookie: {result.StatusCode}");
                    _fetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE, $"[CookieClient] 无法获取 Cookie: {result.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _fetcher.Log($"[CookieClient] 无法获取 Cookie: {ex}");
                _fetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE, $"[CookieClient] 无法获取 Cookie: {ex.Message}");
            }
            return false;
        }
        public async Task<bool> UpdateUserInfoAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bilibili.com/x/web-interface/nav");
                request.Headers.Add("Cookie", _cookie);
                var result = await Utils.client.SendAsync(request);
                var json = JObject.Parse(await result.Content.ReadAsStringAsync());
                if (json["code"].Value<int>() == 0)
                {
                    userInfo = json["data"];
                    uId = userInfo["mid"].Value<long>();

                    _fetcher.Errors.Remove(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_USER_INFO);
                    return true;
                }
                else
                {
                    _fetcher.Log($"[CookieClient] 无法获取用户信息: {json["message"]}");
                    _fetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_USER_INFO, $"无法从 Cookie 获取用户信息: {json["message"]}");
                }
            }
            catch (Exception ex)
            {
                _fetcher.Log($"[CookieClient] 无法获取用户信息: {ex}");
                _fetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_USER_INFO, $"无法从 Cookie 获取用户信息: {ex.Message}");
            }
            return false;
        }
    }
}
