using System.Net;
using Newtonsoft.Json;

namespace VtsuruEventFetcher.Net
{
    public class VTsuruEventFetcherWorker(ILogger<VTsuruEventFetcherWorker> _logger) : BackgroundService
    {
        private readonly List<EventFetcher> _instances = new();
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Utils.Log($"VTsuruEventFetcher 开始运行{(Utils.IsDockerEnv ? ", 当前环境为 Docker 环境" : "")}");

            // Start health probe once if PORT is set
            var port = Environment.GetEnvironmentVariable("PORT");
            if (!string.IsNullOrEmpty(port))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var listener = new HttpListener();
                        listener.Prefixes.Add($"http://*:{port}/");
                        listener.Start();
                        Console.WriteLine($"正在监听端口以通过健康监测: {port}");
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            var context = await listener.GetContextAsync();
                            var response = context.Response;
                            var responseString = "VTsuruEventFetcher";
                            var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, stoppingToken);
                            response.OutputStream.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Health probe failed: {ex.Message}");
                    }
                }, stoppingToken);
            }

            // Initialize global CookieCloud/BILI cookie settings once (alongside token reading)
            try
            {
                string? ccKey = null, ccPwd = null, ccHost = null, biliCookie = null;
                var configPathForCookie = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "config.json");
                if (File.Exists(configPathForCookie))
                {
                    try
                    {
                        var cfg = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPathForCookie));
                        ccKey = string.IsNullOrWhiteSpace(cfg?.CookieCloudKey) ? null : cfg.CookieCloudKey.Trim();
                        ccPwd = string.IsNullOrWhiteSpace(cfg?.CookieCloudPassword) ? null : cfg.CookieCloudPassword.Trim();
                        ccHost = string.IsNullOrWhiteSpace(cfg?.CookieCloudHost) ? null : cfg.CookieCloudHost.Trim();
                        biliCookie = string.IsNullOrWhiteSpace(cfg?.Cookie) ? null : cfg.Cookie.Trim();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"读取配置文件失败: {ex.Message}");
                    }
                }

                var cookieCloudEnv = Environment.GetEnvironmentVariable("COOKIE_CLOUD")?.Trim();
                if (!string.IsNullOrEmpty(cookieCloudEnv))
                {
                    if (!cookieCloudEnv.Contains('@'))
                    {
                        _logger.LogError($"无效的 CookieCloud 秘钥, 应为 KEY@PASSWORD (用户key + @ + 端到端加密密码)");
                        throw new InvalidOperationException("无效的 CookieCloud 秘钥");
                    }
                    var parts = cookieCloudEnv.Split('@');
                    if (parts.Length >= 2)
                    {
                        ccKey = parts[0];
                        ccPwd = parts[1];
                    }
                }

                var biliCookieEnv = Environment.GetEnvironmentVariable("BILI_COOKIE")?.Trim();
                if (!string.IsNullOrEmpty(biliCookieEnv))
                {
                    biliCookie = biliCookieEnv;
                }

                var hostEnv = Environment.GetEnvironmentVariable("COOKIE_CLOUD_HOST")?.Trim();
                if (!string.IsNullOrEmpty(hostEnv))
                {
                    ccHost = hostEnv;
                }

                if (!string.IsNullOrEmpty(ccHost))
                {
                    try
                    {
                        var _ = new Uri(ccHost);
                    }
                    catch
                    {
                        _logger.LogError($"无效的自定义 CookieCloud Host");
                        throw new InvalidOperationException("无效的自定义 CookieCloud Host");
                    }
                }

                EventFetcher.COOKIE_CLOUD_KEY = ccKey;
                EventFetcher.COOKIE_CLOUD_PASSWORD = ccPwd;
                EventFetcher.COOKIE_CLOUD_HOST = ccHost;
                EventFetcher.BILI_COOKIE = biliCookie;
            }
            catch (Exception)
            {
                // rethrow to stop the service early when cookie configuration is invalid
                throw;
            }

            if (!string.IsNullOrEmpty(EventFetcher.COOKIE_CLOUD_KEY) && !string.IsNullOrEmpty(EventFetcher.COOKIE_CLOUD_PASSWORD))
            {
                Utils.Log("已设置 CookieCloud, 将从云端获取 Cookie");
            }
            else if (!string.IsNullOrEmpty(EventFetcher.BILI_COOKIE))
            {
                Utils.Log("已设置逸站 Cookie, 将使用此账户连接直播间");
            }
            else
            {
                Utils.Log("未设置 Cookie 或 CookieCloud, 将使用开放平台进行连接");
            }

            // Parse tokens: support comma-separated tokens from env var or config.json
            var tokens = new List<string>();
            var tokensEnv = Environment.GetEnvironmentVariable("VTSURU_TOKEN");
            if (!string.IsNullOrWhiteSpace(tokensEnv))
            {
                tokens = tokensEnv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(t => t.Trim())
                                   .Where(t => t.Length > 0)
                                   .ToList();
                if(tokens.Count > 0)
                {
                    Utils.Log($"已从环境变量读取到 {tokens.Count} 个 Token: {string.Join(", ", tokens.Select(t => EventFetcher.MaskToken(t)))}");
                }
            }
            if (tokens.Count == 0)
            {
                try
                {
                    var configPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "config.json");
                    if (File.Exists(configPath))
                    {
                        var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                        var tokenCfg = config?.Token?.Trim();
                        if (!string.IsNullOrEmpty(tokenCfg))
                        {
                            tokens = tokenCfg.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                             .Select(t => t.Trim())
                                             .Where(t => t.Length > 0)
                                             .ToList();
                            if (tokens.Count > 0)
                            {
                                Utils.Log($"已从配置文件读取到 {tokens.Count} 个 Token: {string.Join(", ", tokens.Select(t => EventFetcher.MaskToken(t)))}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"读取配置文件失败: {ex.Message}");
                }
            }

            if (tokens.Count == 0)
            {
                _logger.LogInformation("未提供 Token");
                throw new InvalidOperationException("未提供 Token");
            }
            else
            {
                foreach (var t in tokens)
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var f = new EventFetcher();
                            f.Init(_logger, t);
                            _instances.Add(f);
                            _logger.LogInformation($"启动实例成功: {f.GetInstanceLabel()}");
                        }
                        catch (Exception ex)
                        {
                            Utils.Log($"启动实例失败 Token={EventFetcher.MaskToken(t)}: {ex.Message}");
                        }
                    }, stoppingToken);
                    await Task.Delay(2000);
                }
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    //_logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }
                await Task.Delay(1000, stoppingToken);
            }
            // graceful stop
            foreach (var f in _instances)
            {
                try { await f.StopAsync(); } catch { }
            }
            Environment.Exit(0);
        }
    }
}
