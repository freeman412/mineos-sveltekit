using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IWorldService
{
    /// <summary>
    /// List all worlds for a server (world, world_nether, world_the_end)
    /// </summary>
    Task<IReadOnlyList<WorldDto>> ListWorldsAsync(string serverName, CancellationToken cancellationToken);

    /// <summary>
    /// Get detailed information about a specific world
    /// </summary>
    Task<WorldInfoDto> GetWorldInfoAsync(string serverName, string worldName, CancellationToken cancellationToken);

    /// <summary>
    /// Download a world as a ZIP archive
    /// </summary>
    Task<Stream> DownloadWorldAsync(string serverName, string worldName, CancellationToken cancellationToken);

    /// <summary>
    /// Upload and replace a world from a ZIP archive
    /// </summary>
    Task UploadWorldAsync(string serverName, string worldName, Stream zipStream, CancellationToken cancellationToken);

    /// <summary>
    /// Delete/reset a world folder
    /// </summary>
    Task DeleteWorldAsync(string serverName, string worldName, CancellationToken cancellationToken);

    /// <summary>
    /// Get world folder size in bytes
    /// </summary>
    Task<long> GetWorldSizeAsync(string serverName, string worldName, CancellationToken cancellationToken);
}
