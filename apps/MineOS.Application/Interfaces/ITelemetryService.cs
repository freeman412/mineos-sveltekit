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

    /// <summary>
    /// Reports a standardized error event with structured metadata.
    /// </summary>
    Task ReportErrorAsync(
        string errorCode,
        string errorMessage,
        string? stackTrace = null,
        string severity = "medium",
        string? serverName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports an update lifecycle event.
    /// </summary>
    Task ReportUpdateEventAsync(
        string eventType,
        string fromVersion,
        string toVersion,
        CancellationToken cancellationToken = default);
}

public record UsageData(
    int ServerCount,
    int ActiveServerCount,
    int TotalUserCount,
    int ActiveUserCount,
    long UptimeSeconds,
    // New fields
    string[]? ServerTypes = null,
    int? BackupCount = null,
    bool? LastBackupSuccess = null,
    int? BackupTotalSizeMb = null,
    Dictionary<string, int>? FeatureUsage = null);
