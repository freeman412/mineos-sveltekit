using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;
using ServerStatusStrings = MineOS.Infrastructure.Constants.ServerStatus;

namespace MineOS.Infrastructure.Background;

public sealed class TelemetryReporterService : BackgroundService, ITelemetryReportTrigger
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryReporterService> _logger;
    private readonly Channel<bool> _triggerChannel;

    public TelemetryReporterService(
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryReporterService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _triggerChannel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public void RequestImmediateReport()
    {
        _triggerChannel.Writer.TryWrite(true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telemetry reporter started, first report in {Delay} seconds", InitialDelay.TotalSeconds);

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
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(ReportInterval);
                await _triggerChannel.Reader.ReadAsync(cts.Token);
                _logger.LogInformation("Immediate telemetry report triggered");
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;
                // Interval elapsed without a trigger — normal periodic report
            }
        }
    }

    public async Task ReportTelemetryAsync(CancellationToken cancellationToken)
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
        var featureTracker = scope.ServiceProvider.GetRequiredService<IFeatureUsageTracker>();

        var servers = await serverService.ListServersAsync(cancellationToken);
        var users = await userService.ListUsersAsync(cancellationToken);

        var activeServerCount = 0;
        var serverTypes = new List<string>();

        foreach (var server in servers)
        {
            try
            {
                var status = await serverService.GetServerStatusAsync(server.Name, cancellationToken);
                if (status.Status == ServerStatusStrings.Running)
                    activeServerCount++;

                // Extract server type from profile (e.g., "paper", "vanilla", "forge")
                var profile = server.Config?.Minecraft?.Profile;
                if (!string.IsNullOrEmpty(profile))
                    serverTypes.Add(profile.ToLowerInvariant());
            }
            catch (Exception ex)
            {
                // Log but don't fail telemetry for individual server errors
                _logger.LogDebug(ex, "Failed to get status for server {ServerName}", server.Name);
            }
        }

        // Gather backup health
        int? backupCount = null;
        bool? lastBackupSuccess = null;
        int? backupTotalSizeMb = null;

        try
        {
            var allBackups = new List<(bool success, long sizeBytes)>();
            foreach (var server in servers)
            {
                try
                {
                    var backups = await backupService.ListBackupsAsync(server.Name, cancellationToken);
                    foreach (var backup in backups)
                    {
                        allBackups.Add((true, backup.Size ?? 0));
                    }
                }
                catch
                {
                    // Ignore per-server backup errors
                }
            }

            if (allBackups.Count > 0)
            {
                backupCount = allBackups.Count;
                lastBackupSuccess = true; // If we can list them, they exist
                backupTotalSizeMb = (int)(allBackups.Sum(b => b.sizeBytes) / (1024 * 1024));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to gather backup health");
        }

        var uptimeSeconds = (long)Stopwatch.GetElapsedTime(StartTimestamp).TotalSeconds;
        var featureUsage = featureTracker.GetAndReset();

        var data = new UsageData(
            ServerCount: servers.Count,
            ActiveServerCount: activeServerCount,
            TotalUserCount: users.Count,
            ActiveUserCount: users.Count,
            UptimeSeconds: uptimeSeconds,
            ServerTypes: serverTypes.Count > 0 ? serverTypes.ToArray() : null,
            BackupCount: backupCount,
            LastBackupSuccess: lastBackupSuccess,
            BackupTotalSizeMb: backupTotalSizeMb,
            FeatureUsage: featureUsage.Count > 0 ? featureUsage : null);

        _logger.LogInformation("Sending usage telemetry report...");
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
        await telemetryService.ReportUsageAsync(data, cancellationToken);
    }
}
