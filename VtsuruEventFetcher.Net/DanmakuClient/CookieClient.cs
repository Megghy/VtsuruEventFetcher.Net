using System.Text;
using BDanMuLib;
using Newtonsoft.Json.Linq;

namespace VtsuruEventFetcher.Net.DanmakuClient
{
    internal class CookieClient(string? cookie, string? cookieCloudKey, string? cookieCloudPassword, string? cookieCloudHost) : IDanmakuClient
    {
        DanMuCore _danmu;
        public async Task Connect()
        {
            var danmu = new DanMuCore(DanMuCore.ClientType.Wss, EventFetcher.roomId, EventFetcher.uId, cookie, uId);
            danmu.ReceiveMessage += Danmu_ReceiveMessage; ;
            danmu.OnDisconnect += () =>
            {
                _danmu = null;
                _ = Init();
                _ = Connect();
            };
            while (!(await danmu.ConnectAsync()))
            {
                Utils.Log("[CookieClient] 无法连接直播间, 10秒后重试");
                await Task.Delay(10000);
            }
            _danmu = danmu;
            Utils.Log($"[CookieClient] 已连接直播间: {EventFetcher.roomId}");
        }

        private void Danmu_ReceiveMessage(long roomId, BDanmuLib.Models.MessageType messageType, BDanMuLib.Models.IBaseMessage obj)
        {
            if(obj is not null)
            {
                EventFetcher.AddEvent(obj.Metadata.ToString());
            }
        }

        public void Dispose()
        {
            _danmu?.Disconnect();
            _danmu = null;
            updateCookieTimer?.Dispose();
            updateCookieTimer = null;
        }


        System.Timers.Timer updateCookieTimer;
        string cookie = cookie;
        string cookieCloudKey = cookieCloudKey;
        string cookieCloudPassword = cookieCloudPassword;
        private readonly string cookieCloudHost = string.IsNullOrEmpty(cookieCloudHost) ? "https://cookie.suki.club/" : cookieCloudHost;

        JToken userInfo;
        long uId;
        public async Task Init()
        {
            if (updateCookieTimer is null)
            {
                updateCookieTimer = new()
                {
                    Interval = TimeSpan.FromSeconds(120).TotalMilliseconds,
                    AutoReset = true,
                };
                updateCookieTimer.Elapsed += (_, _) =>
                {
                    _ = UpdateCookieAsync();
                    _ = UpdateUserInfoAsync();
                };
                updateCookieTimer.Start();
            }
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
        }
        public async Task<bool> UpdateCookieAsync()
        {
            try
            {
                var uri = new Uri(new Uri(cookieCloudHost), $"/get/{cookieCloudKey}");
                var request = new HttpRequestMessage(HttpMethod.Post, uri);

                // 添加要发送的表单数据
                var postData = new List<KeyValuePair<string, string>>
                {
                new("password", cookieCloudPassword),
                };

                // 创建FormUrlEncodedContent（URL编码的内容）
                request.Content = new FormUrlEncodedContent(postData);
                var result = await Utils.client.SendAsync(request);
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
                        cookie = cookieStringBuilder.ToString();
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
                    EventFetcher.Errors.TryAdd(ErrorCodes.COOKIE_CLIENT_UNABLE_GET_COOKIE, "[CookieClient] 无法获取 Cookie: {result.StatusCode}");
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
                request.Headers.Add("Cookie", cookie);
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
