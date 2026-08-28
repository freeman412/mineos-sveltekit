using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IModrinthService
{
    Task<ModrinthSearchResultDto> SearchModsAsync(
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        string? sortBy,
        CancellationToken cancellationToken);

    Task<ModrinthSearchResultDto> SearchModpacksAsync(
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        string? sortBy,
        CancellationToken cancellationToken);

    Task<ModrinthSearchResultDto> SearchPluginsAsync(
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        string? sortBy,
        CancellationToken cancellationToken);

    Task<ModrinthSearchResultDto> SearchResourcePacksAsync(
        string query,
        int index,
        int pageSize,
        string? gameVersion,
        string? sortBy,
        CancellationToken cancellationToken);

    Task<ModrinthProjectDto?> GetProjectAsync(string projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModrinthVersionDto>> GetProjectVersionsAsync(
        string projectId,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken);

    Task<ModrinthVersionDto?> GetVersionAsync(string versionId, CancellationToken cancellationToken);
    Task<ModrinthVersionDto?> GetVersionByFileHashAsync(string hash, string algorithm, CancellationToken cancellationToken);

    /// <summary>
    /// Identifies many local files at once. Used to work out which mods a server
    /// already has: hashing the jars and asking Modrinth in one call is the only
    /// reliable way, since a file's name says nothing about which project it is.
    ///
    /// Returns only the hashes Modrinth recognises; unknown files are simply absent.
    /// </summary>
    Task<IReadOnlyDictionary<string, ModrinthVersionDto>> GetVersionsByFileHashesAsync(
        IReadOnlyList<string> hashes,
        string algorithm,
        CancellationToken cancellationToken);

    Task<Stream> OpenDownloadStreamAsync(string url, CancellationToken cancellationToken);
}
