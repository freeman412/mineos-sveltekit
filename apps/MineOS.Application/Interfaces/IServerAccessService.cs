using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IServerAccessService
{
    Task<ServerAccessDto?> GetAccessAsync(int userId, string serverName, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServerAccessDto>> ListAccessAsync(int userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListServerNamesAsync(int userId, CancellationToken cancellationToken);
}
