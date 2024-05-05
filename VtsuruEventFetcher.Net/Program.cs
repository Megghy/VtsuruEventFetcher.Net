using VtsuruEventFetcher.Net;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<VTsuruEventFetcherWorker>();
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "VtsuruEventFetcher Service";
});

var host = builder.Build();

AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
{
    EventFetcher._client?.Dispose();
    _ = EventFetcher._hub?.DisposeAsync();
};

host.Run();
