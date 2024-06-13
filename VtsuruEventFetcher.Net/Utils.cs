namespace VtsuruEventFetcher.Net
{
    internal static class Utils
    {
        public static HttpClient client = new(new HttpClientHandler()
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        public static string LogPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "logs");
        public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (!request.Headers.Contains("User-Agent"))
                request.Headers.Add("User-Agent", EventFetcher.User_Agent);
            return await client.SendAsync(request);
        }
        public static async Task<string?> GetAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", EventFetcher.User_Agent);
            return await (await SendAsync(request))?.Content.ReadAsStringAsync();
        }
        public static void Log(string msg)
        {
            try
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} - {msg}");
                if (EventFetcher.IsDockerEnv)
                {
                    return;
                }
                if (!Directory.Exists(LogPath))
                {
                    Directory.CreateDirectory(LogPath);
                }
                var path = Path.Combine(LogPath, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} - {msg}" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                EventFetcher._logger?.LogError(ex.Message, ex);
            }
        }
        internal static void ClearLog()
        {
            if (EventFetcher.IsDockerEnv)
            {
                return;
            }
            try
            {
                if (!Directory.Exists(LogPath))
                {
                    Directory.CreateDirectory(LogPath);
                }
                
                var files = Directory.GetFiles(LogPath);
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < DateTime.Now.AddDays(-7) || fileInfo.Length > 10 * 1024 * 1024)
                    {
                        File.Delete(file);
                        Console.WriteLine($"已删除无效日志文件 {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                EventFetcher._logger?.LogError(ex.Message, ex);
            }
        }
    }
}
