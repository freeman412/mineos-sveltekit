using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Background;

public sealed class TelemetryReporterService : BackgroundService
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromHours(24); // Report once per day
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5); // Wait 5 minutes after startup

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
        // Wait a bit before first report to allow system to stabilize
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
                _logger.LogDebug(ex, "Telemetry reporting cycle failed");
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
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
        await telemetryService.ReportUsageAsync(cancellationToken);
    }
}
