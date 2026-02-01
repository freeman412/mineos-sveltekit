namespace MineOS.Application.Interfaces;

public interface ITelemetryService
{
    /// <summary>
    /// Reports usage telemetry asynchronously (non-blocking)
    /// </summary>
    Task ReportUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports a lifecycle event (startup, shutdown, server_created, etc.)
    /// </summary>
    Task ReportLifecycleEventAsync(string eventType, object? metadata = null, CancellationToken cancellationToken = default);
}
