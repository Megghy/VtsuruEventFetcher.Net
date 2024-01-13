using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public static void Log(string msg)
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
                EventFetcher._logger?.LogError(ex.Message, ex);
            }
        }
    }
}
