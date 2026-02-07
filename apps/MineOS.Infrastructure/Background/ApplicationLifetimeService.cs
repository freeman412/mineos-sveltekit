using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Background;

public sealed class ApplicationLifetimeService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10); // Wait 10 seconds after startup

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApplicationLifetimeService> _logger;
    private readonly TelemetryReporterService _telemetryReporter;
    private readonly DateTime _startTime;

    public ApplicationLifetimeService(
        IServiceScopeFactory scopeFactory,
        ILogger<ApplicationLifetimeService> logger,
        TelemetryReporterService telemetryReporter)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _telemetryReporter = telemetryReporter;
        _startTime = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before sending startup event to allow system to stabilize
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Send startup event
        try
        {
            await ReportStartupAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report application startup event");
        }

        // Wait for shutdown signal
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when application is shutting down
        }

        // Send shutdown event
        try
        {
            var uptime = DateTime.UtcNow - _startTime;
            await ReportShutdownAsync(uptime);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report application shutdown event");
        }

        // Send final usage report to capture data for short-lived instances
        try
        {
            _logger.LogInformation("Sending final telemetry usage report before shutdown...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _telemetryReporter.ReportTelemetryAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send final telemetry usage report on shutdown");
        }
    }

    private async Task ReportStartupAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var metadata = new
        {
            startup_time = _startTime
        };

        _logger.LogInformation("Reporting application startup event");
        await telemetryService.ReportLifecycleEventAsync("startup", metadata, cancellationToken);
    }

    private async Task ReportShutdownAsync(TimeSpan uptime)
    {
        using var scope = _scopeFactory.CreateScope();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

        var metadata = new
        {
            uptime_seconds = (int)uptime.TotalSeconds
        };

        _logger.LogInformation("Reporting application shutdown event (uptime: {Uptime})", uptime);

        // Use a short timeout for shutdown event to avoid delaying shutdown
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await telemetryService.ReportLifecycleEventAsync("shutdown", metadata, cts.Token);
    }
}
