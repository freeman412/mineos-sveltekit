using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IWatchdogService
{
    /// <summary>
    /// Get recent crash events for a server
    /// </summary>
    Task<IEnumerable<CrashEventDto>> GetCrashEventsAsync(string serverName, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent crash events across all servers
    /// </summary>
    Task<IEnumerable<CrashEventDto>> GetAllCrashEventsAsync(int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear crash history for a server
    /// </summary>
    Task ClearCrashHistoryAsync(string serverName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current watchdog status for all monitored servers
    /// </summary>
    Dictionary<string, ServerWatchdogStatus> GetWatchdogStatus();
}

public record ServerWatchdogStatus(
    string ServerName,
    bool IsMonitoring,
    bool WasRunning,
    int RestartAttempts,
    DateTimeOffset? LastCrashTime,
    DateTimeOffset? LastRestartAttempt,
    DateTimeOffset? CooldownEndsAt);
