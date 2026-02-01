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
        CancellationToken cancellationToken);

    Task<ModrinthSearchResultDto> SearchModpacksAsync(
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken);

    Task<ModrinthSearchResultDto> SearchPluginsAsync(
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken);

    Task<ModrinthSearchResultDto> SearchResourcePacksAsync(
        string query,
        int index,
        int pageSize,
        string? gameVersion,
        CancellationToken cancellationToken);

    Task<ModrinthProjectDto?> GetProjectAsync(string projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModrinthVersionDto>> GetProjectVersionsAsync(
        string projectId,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken);

    Task<ModrinthVersionDto?> GetVersionAsync(string versionId, CancellationToken cancellationToken);
    Task<ModrinthVersionDto?> GetVersionByFileHashAsync(string hash, string algorithm, CancellationToken cancellationToken);

    Task<Stream> OpenDownloadStreamAsync(string url, CancellationToken cancellationToken);
}
