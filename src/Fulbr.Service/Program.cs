using Fulbr.Service;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "Fulbr")
    .ConfigureServices(services =>
    {
        services.AddSingleton<TunnelEngine>();
        services.AddSingleton<NamedPipeServer>();
        services.AddHostedService<TunnelWorker>();
    });

var host = builder.Build();
await host.RunAsync();
