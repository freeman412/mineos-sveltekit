using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IMonitoringService
{
    IAsyncEnumerable<ServerHeartbeatDto> StreamHeartbeatAsync(string serverName, CancellationToken cancellationToken);
    Task<PingInfoDto?> GetPingInfoAsync(string serverName, CancellationToken cancellationToken);
    Task<QueryInfoDto?> GetQueryInfoAsync(string serverName, CancellationToken cancellationToken);
    Task<DetailedMemoryInfoDto> GetMemoryInfoAsync(string serverName, CancellationToken cancellationToken);
}
