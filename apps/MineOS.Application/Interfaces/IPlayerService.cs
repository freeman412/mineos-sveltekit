using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IPlayerService
{
    Task<IReadOnlyList<PlayerSummaryDto>> ListPlayersAsync(string serverName, CancellationToken cancellationToken);
    Task<PlayerStatsDto> GetPlayerStatsAsync(string serverName, string uuid, CancellationToken cancellationToken);
    Task WhitelistPlayerAsync(string serverName, string uuid, string? name, CancellationToken cancellationToken);
    Task RemoveWhitelistAsync(string serverName, string uuid, CancellationToken cancellationToken);
    Task OpPlayerAsync(
        string serverName,
        string uuid,
        string? name,
        int level,
        bool bypassesPlayerLimit,
        CancellationToken cancellationToken);
    Task DeopPlayerAsync(string serverName, string uuid, CancellationToken cancellationToken);
    Task BanPlayerAsync(
        string serverName,
        string uuid,
        string? name,
        string? reason,
        string? bannedBy,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken);
    Task UnbanPlayerAsync(string serverName, string uuid, CancellationToken cancellationToken);
}
