using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

/// <summary>
/// Service for tracking and querying player activity from server logs.
/// </summary>
public interface IPlayerActivityService
{
    /// <summary>
    /// Processes the latest log file for a server to extract player activity events.
    /// </summary>
    Task ProcessServerLogsAsync(string serverName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent activity events for a server.
    /// </summary>
    Task<IReadOnlyList<PlayerActivityEventDto>> GetRecentActivityAsync(
        string serverName,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets activity events for a specific player.
    /// </summary>
    Task<IReadOnlyList<PlayerActivityEventDto>> GetPlayerActivityAsync(
        string serverName,
        string playerUuid,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session history for a specific player.
    /// </summary>
    Task<IReadOnlyList<PlayerSessionDto>> GetPlayerSessionsAsync(
        string serverName,
        string playerUuid,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent sessions for a server (all players).
    /// </summary>
    Task<IReadOnlyList<PlayerSessionDto>> GetRecentSessionsAsync(
        string serverName,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets player activity statistics.
    /// </summary>
    Task<PlayerActivityStatsDto> GetPlayerActivityStatsAsync(
        string serverName,
        string playerUuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes any open sessions when server stops.
    /// </summary>
    Task CloseOpenSessionsAsync(string serverName, string reason = "server_stop", CancellationToken cancellationToken = default);
}
