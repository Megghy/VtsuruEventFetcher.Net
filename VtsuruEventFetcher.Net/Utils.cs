using System.Reflection;
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
        internal static readonly string DefaultUserAgent = $"VTsuruEventFetcher/{Assembly.GetExecutingAssembly().GetName().Version} ({Environment.OSVersion})";
        
        // Detect docker environment globally
        internal static readonly bool IsDockerEnv = File.Exists("/.dockerenv")
                                       || IsDockerCGroupPresent()
                                       || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        
        private static bool IsDockerCGroupPresent()
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
                // ignore
            }
            return false;
        }
        public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (!request.Headers.Contains("User-Agent"))
                request.Headers.Add("User-Agent", DefaultUserAgent);
            return await client.SendAsync(request);
        }
        public static async Task<string?> GetAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", DefaultUserAgent);
            return await (await SendAsync(request))?.Content.ReadAsStringAsync();
        }
        public static void Log(string msg)
        {
            try
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} - {msg}");
                if (IsDockerEnv)
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
                Console.WriteLine($"Log error: {ex.Message}");
            }
        }
        internal static void ClearLog()
        {
            if (IsDockerEnv)
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
                Console.WriteLine($"ClearLog error: {ex.Message}");
            }
        }
    }
}
