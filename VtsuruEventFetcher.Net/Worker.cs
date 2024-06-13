namespace VtsuruEventFetcher.Net
{
    public class VTsuruEventFetcherWorker(ILogger<VTsuruEventFetcherWorker> _logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Utils.Log($"VTsuruEventFetcher 开始运行{(EventFetcher.IsDockerEnv ? ", 当前环境为 Docker 环境" : "")}");
            EventFetcher.Init(_logger);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    //_logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }
                await Task.Delay(1000, stoppingToken);
            }
            Environment.Exit(0);
        }
    }
}
