using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Background;

public sealed class StartupServerService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StartupServerService> _logger;

    public StartupServerService(IServiceScopeFactory scopeFactory, ILogger<StartupServerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Startup server service starting");

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var serverService = scope.ServiceProvider.GetRequiredService<IServerService>();
        var processManager = scope.ServiceProvider.GetRequiredService<IProcessManager>();

        var servers = await serverService.ListServersAsync(stoppingToken);
        foreach (var server in servers)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var config = server.Config;
            if (config?.OnReboot.Start != true)
            {
                continue;
            }

            try
            {
                var isRunning = await processManager.IsServerRunningAsync(server.Name, stoppingToken);
                if (isRunning)
                {
                    continue;
                }

                _logger.LogInformation("Auto-starting server {ServerName} (on reboot enabled)", server.Name);
                await serverService.StartServerAsync(server.Name, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-start server {ServerName}", server.Name);
            }
        }

        _logger.LogInformation("Startup server service finished");
    }
}
