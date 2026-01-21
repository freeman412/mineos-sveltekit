using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IModService
{
    Task<IReadOnlyList<InstalledModDto>> ListModsAsync(string serverName, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstalledModWithModpackDto>> ListModsWithModpacksAsync(string serverName, CancellationToken cancellationToken);
    Task SaveModAsync(string serverName, string fileName, Stream content, CancellationToken cancellationToken);
    Task DeleteModAsync(string serverName, string fileName, CancellationToken cancellationToken);
    Task<string> GetModPathAsync(string serverName, string fileName, CancellationToken cancellationToken);
    Task InstallModFromCurseForgeAsync(
        string serverName,
        int modId,
        int? fileId,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken);
    Task InstallModpackAsync(
        string serverName,
        int modpackId,
        int? fileId,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken);

    Task InstallModrinthModpackAsync(
        string serverName,
        string projectId,
        string versionId,
        string? projectName,
        string? projectVersion,
        string? logoUrl,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken);

    // Enhanced modpack methods with state tracking and rollback
    Task InstallModpackWithStateAsync(
        string serverName,
        int modpackId,
        int? fileId,
        string modpackName,
        string? modpackVersion,
        string? logoUrl,
        IModpackInstallState state,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<InstalledModpackDto>> ListInstalledModpacksAsync(string serverName, CancellationToken cancellationToken);
    Task UninstallModpackAsync(string serverName, int modpackDbId, CancellationToken cancellationToken);
}
