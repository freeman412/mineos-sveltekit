using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IServerService
{
    // Server CRUD
    Task<ServerDetailDto> GetServerAsync(string name, CancellationToken cancellationToken);
    Task<ServerDetailDto> CreateServerAsync(CreateServerRequest request, string username, CancellationToken cancellationToken);
    Task DeleteServerAsync(string name, CancellationToken cancellationToken);

    // Lifecycle operations
    Task<ServerHeartbeatDto> GetServerStatusAsync(string name, CancellationToken cancellationToken);
    Task StartServerAsync(string name, CancellationToken cancellationToken);
    Task StopServerAsync(string name, int timeoutSeconds, CancellationToken cancellationToken);
    Task RestartServerAsync(string name, CancellationToken cancellationToken);
    Task KillServerAsync(string name, CancellationToken cancellationToken);

    // Configuration management
    Task<Dictionary<string, string>> GetServerPropertiesAsync(string name, CancellationToken cancellationToken);
    Task UpdateServerPropertiesAsync(string name, Dictionary<string, string> properties, CancellationToken cancellationToken);
    Task<ServerConfigDto> GetServerConfigAsync(string name, CancellationToken cancellationToken);
    Task UpdateServerConfigAsync(string name, ServerConfigDto config, CancellationToken cancellationToken);
}
