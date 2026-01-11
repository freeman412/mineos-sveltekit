using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Background;

public sealed class PerformanceCollectorService : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HostOptions _hostOptions;
    private readonly ILogger<PerformanceCollectorService> _logger;

    public PerformanceCollectorService(
        IServiceScopeFactory scopeFactory,
        IOptions<HostOptions> hostOptions,
        ILogger<PerformanceCollectorService> logger)
    {
        _scopeFactory = scopeFactory;
        _hostOptions = hostOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Performance collection cycle failed");
            }

            try
            {
                await Task.Delay(SampleInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CollectOnceAsync(CancellationToken cancellationToken)
    {
        var serversPath = Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment);
        if (!Directory.Exists(serversPath))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var performanceService = scope.ServiceProvider.GetRequiredService<IPerformanceService>();

        foreach (var dir in Directory.EnumerateDirectories(serversPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            await performanceService.RecordSampleAsync(name, cancellationToken);
        }
    }
}
