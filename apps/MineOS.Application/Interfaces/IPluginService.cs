using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IPluginService
{
    Task<IReadOnlyList<InstalledPluginDto>> ListPluginsAsync(string serverName, CancellationToken cancellationToken);
    Task SavePluginAsync(string serverName, string fileName, Stream content, CancellationToken cancellationToken);
    Task DeletePluginAsync(string serverName, string fileName, CancellationToken cancellationToken);
    Task<string> GetPluginPathAsync(string serverName, string fileName, CancellationToken cancellationToken);
    Task SetPluginEnabledAsync(string serverName, string fileName, bool enabled, CancellationToken cancellationToken);
}
