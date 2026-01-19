using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IClientPackageService
{
    Task<IEnumerable<ClientPackageEntryDto>> ListClientPackagesAsync(string serverName, CancellationToken cancellationToken);
    Task<string> CreateClientPackageAsync(string serverName, CancellationToken cancellationToken);
    Task DeleteClientPackageAsync(string serverName, string filename, CancellationToken cancellationToken);
    Task<string> GetClientPackagePathAsync(string serverName, string filename, CancellationToken cancellationToken);
}
