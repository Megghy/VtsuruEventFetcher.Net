using System.Text;
using BDanMuLib;
using Newtonsoft.Json.Linq;

namespace VtsuruEventFetcher.Net.DanmakuClient
{
    internal class CookieClient(string? cookie, string? cookieCloudKey, string? cookieCloudPassword, string? cookieCloudHost) : IDanmakuClient
    {
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
                var danmu = new DanMuCore(DanMuCore.ClientType.Wss, EventFetcher.roomId, EventFetcher.uId, _cookie, uId);
                danmu.ReceiveRawMessage += Danmu_ReceiveRawMessage;
                danmu.OnDisconnect += () => _ = Task.Run(OnClose);
                if (!await danmu.ConnectAsync())
                {
                    isConnecting = false;
                    throw new("[CookieClient] 无法连接到直播间");
                }
                _danmu = danmu;
                _isRunning = true;
                EventFetcher.Errors.Remove(ErrorCodes.CLIENT_DISCONNECTED);
                Utils.Log($"[CookieClient] 已连接直播间: {EventFetcher.roomId}");
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

            EventFetcher.Errors.TryAdd(ErrorCodes.CLIENT_DISCONNECTED, $"Cookie 弹幕客户端已断开连接");

            Dispose();

            Utils.Log($"[CookieClient] 连接断开, 将重新连接");
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
                    Utils.Log($"[CookieClient] 无法重新连接, 10秒后重试: {ex.Message}");
                    Thread.Sleep(10000);
                }
            }
            Utils.Log($"[CookieClient] 已重新连接");
        }
        private bool Danmu_ReceiveRawMessage(long roomId, string msg)
        {
            if (!string.IsNullOrEmpty(msg))
            {
                EventFetcher.AddEvent(msg);
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
        string _cookie = cookie;
        readonly string _cookieCloudKey = cookieCloudKey;
        readonly string _cookieCloudPassword = cookieCloudPassword;
        private readonly string _cookieCloudHost = string.IsNullOrEmpty(cookieCloudHost) ? "https://cookie.suki.club/" : cookieCloudHost;

        JToken userInfo;
        long uId;
        public async Task Init()
        {
            while (!(await UpdateCookieAsync()))
            {
                Utils.Log("[CookieClient] 无法从 CookieCloud 获取 Cookie, 10秒后重试");
                await Task.Delay(10000);
            }
            Utils.Log("[CookieClient] 已从 CookieCloud 获取 Cookie");
            while (!(await UpdateUserInfoAsync()))
            {
                Utils.Log("[CookieClient] 无法获取用户信息, 10秒后重试");
                await Task.Delay(10000);
            }
            if (_updateCookieTimer is null)
            {
                _updateCookieTimer = new()
                {
                    Interval = TimeSpan.FromSeconds(120).TotalMilliseconds,
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
                        }
                        _cookie = cookieStringBuilder.ToString();
                        EventFetcher.Errors.Remove(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE);
                        return true;
                    }
                    else
                    {
                        Utils.Log($"[CookieClient] 已从 CookieCloud 中获取数据, 但其中不存在 BiliBili 的 Cookie");
                        EventFetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE, "已从 CookieCloud 中获取数据, 但其中不存在 BiliBili 的 Cookie");
                    }
                }
                else
                {
                    Utils.Log($"[CookieClient] 无法获取 Cookie: {result.StatusCode}");
                    EventFetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE, $"[CookieClient] 无法获取 Cookie: {result.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Utils.Log($"[CookieClient] 无法获取 Cookie: {ex}");
                EventFetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE, $"[CookieClient] 无法获取 Cookie: {ex.Message}");
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

                    EventFetcher.Errors.Remove(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_USER_INFO);
                    return true;
                }
                else
                {
                    Utils.Log($"[CookieClient] 无法获取用户信息: {json["message"]}");
                    EventFetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_USER_INFO, $"无法从 Cookie 获取用户信息: {json["message"]}");
                }
            }
            catch (Exception ex)
            {
                Utils.Log($"[CookieClient] 无法获取用户信息: {ex}");
                EventFetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_USER_INFO, $"无法从 Cookie 获取用户信息: {ex.Message}");
            }
            return false;
        }
    }
}
