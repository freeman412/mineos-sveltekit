namespace MineOS.Application.Dtos;

public record NeoForgeVersionDto(
    string MinecraftVersion,
    string NeoForgeVersion,
    bool IsLatest,
    DateTimeOffset? ReleaseDate);

public record NeoForgeInstallResultDto(
    string InstallId,
    string Status,
    string? Error);

public record NeoForgeInstallStatusDto(
    string InstallId,
    string MinecraftVersion,
    string NeoForgeVersion,
    string ServerName,
    string Status,
    int Progress,
    string? CurrentStep,
    string? Error,
    string? Output,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
