using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Background;

public sealed class TelemetryReporterService : BackgroundService
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryReporterService> _logger;

    public TelemetryReporterService(
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryReporterService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telemetry reporter started, first report in {Delay} minutes", InitialDelay.TotalMinutes);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportTelemetryAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telemetry reporting cycle failed");
            }

            try
            {
                await Task.Delay(ReportInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReportTelemetryAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var enabled = await settingsService.GetAsync(
            Services.SettingsService.Keys.TelemetryEnabled, cancellationToken);

        if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Telemetry disabled via settings, skipping report");
            return;
        }

        _logger.LogInformation("Gathering usage data for telemetry report...");

        var serverService = scope.ServiceProvider.GetRequiredService<IServerService>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var worldService = scope.ServiceProvider.GetRequiredService<IWorldService>();
        var hostOptions = scope.ServiceProvider.GetRequiredService<IOptions<HostOptions>>().Value;

        var servers = await serverService.ListServersAsync(cancellationToken);
        var users = await userService.ListUsersAsync(cancellationToken);

        var activeServerCount = 0;
        var totalBackupsCount = 0;
        var totalWorldsCount = 0;
        var serversWithModsCount = 0;
        var serversWithPluginsCount = 0;

        foreach (var server in servers)
        {
            try
            {
                var status = await serverService.GetServerStatusAsync(server.Name, cancellationToken);
                if (status.Status == "up")
                    activeServerCount++;

                var backups = await backupService.ListBackupsAsync(server.Name, cancellationToken);
                totalBackupsCount += backups.Count();

                var worlds = await worldService.ListWorldsAsync(server.Name, cancellationToken);
                totalWorldsCount += worlds.Count;

                var serverPath = Path.Combine(hostOptions.BaseDirectory, hostOptions.ServersPathSegment, server.Name);

                if (Directory.Exists(Path.Combine(serverPath, "mods")) &&
                    Directory.GetFiles(Path.Combine(serverPath, "mods"), "*.jar").Length > 0)
                    serversWithModsCount++;

                if (Directory.Exists(Path.Combine(serverPath, "plugins")) &&
                    Directory.GetFiles(Path.Combine(serverPath, "plugins"), "*.jar").Length > 0)
                    serversWithPluginsCount++;
            }
            catch
            {
                // Ignore errors for individual servers
            }
        }

        var data = new UsageData(
            ServerCount: servers.Count,
            ActiveServerCount: activeServerCount,
            TotalUserCount: users.Count,
            TotalBackupsCount: totalBackupsCount,
            TotalWorldsCount: totalWorldsCount,
            ServersWithModsCount: serversWithModsCount,
            ServersWithPluginsCount: serversWithPluginsCount);

        _logger.LogInformation("Sending usage telemetry report...");
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
        await telemetryService.ReportUsageAsync(data, cancellationToken);
    }
}
