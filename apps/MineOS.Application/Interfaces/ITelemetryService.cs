namespace MineOS.Application.Interfaces;

public interface ITelemetryService
{
    /// <summary>
    /// Sends a pre-gathered usage report to the telemetry endpoint.
    /// </summary>
    Task ReportUsageAsync(UsageData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports a lifecycle event (startup, shutdown, server_created, etc.)
    /// </summary>
    Task ReportLifecycleEventAsync(string eventType, object? metadata = null, CancellationToken cancellationToken = default);
}

public record UsageData(
    int ServerCount,
    int ActiveServerCount,
    int TotalUserCount,
    int ActiveUserCount,
    long UptimeSeconds);
