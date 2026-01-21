namespace MineOS.Application.Interfaces;

public interface IForgeService
{
    Task<IReadOnlyList<ForgeVersionDto>> GetVersionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ForgeVersionDto>> GetVersionsForMinecraftAsync(string minecraftVersion, CancellationToken cancellationToken);
    Task<ForgeInstallResultDto> InstallForgeAsync(string minecraftVersion, string forgeVersion, string serverName, CancellationToken cancellationToken);
    Task<ForgeInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken);
    IReadOnlyList<ForgeInstallStatusDto> GetActiveInstalls();
}

public record ForgeVersionDto(
    string MinecraftVersion,
    string ForgeVersion,
    string FullVersion,
    bool IsRecommended,
    bool IsLatest,
    DateTimeOffset? ReleaseDate);

public record ForgeInstallResultDto(
    string InstallId,
    string Status,
    string? Error);

public record ForgeInstallStatusDto(
    string InstallId,
    string MinecraftVersion,
    string ForgeVersion,
    string ServerName,
    string Status,
    int Progress,
    string? CurrentStep,
    string? Error,
    string? Output,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
