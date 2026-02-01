using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;
using ServerStatusStrings = MineOS.Infrastructure.Constants.ServerStatus;

namespace MineOS.Infrastructure.Background;

public sealed class TelemetryReporterService : BackgroundService
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

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

        var servers = await serverService.ListServersAsync(cancellationToken);
        var users = await userService.ListUsersAsync(cancellationToken);

        var activeServerCount = 0;
        foreach (var server in servers)
        {
            try
            {
                var status = await serverService.GetServerStatusAsync(server.Name, cancellationToken);
                if (status.Status == ServerStatusStrings.Running)
                    activeServerCount++;
            }
            catch (Exception ex)
            {
                // Log but don't fail telemetry for individual server errors
                _logger.LogDebug(ex, "Failed to get status for server {ServerName}", server.Name);
            }
        }

        var uptimeSeconds = (long)Stopwatch.GetElapsedTime(StartTimestamp).TotalSeconds;

        var data = new UsageData(
            ServerCount: servers.Count,
            ActiveServerCount: activeServerCount,
            TotalUserCount: users.Count,
            ActiveUserCount: users.Count,
            UptimeSeconds: uptimeSeconds);

        _logger.LogInformation("Sending usage telemetry report...");
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
        await telemetryService.ReportUsageAsync(data, cancellationToken);
    }
}
