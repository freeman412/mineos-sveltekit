namespace MineOS.Application.Interfaces;

public interface ITelemetryService
{
    /// <summary>
    /// Reports usage telemetry asynchronously (non-blocking)
    /// </summary>
    Task ReportUsageAsync(CancellationToken cancellationToken = default);
}
