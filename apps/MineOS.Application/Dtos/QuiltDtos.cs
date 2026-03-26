namespace MineOS.Application.Dtos;

public record QuiltGameVersionDto(
    string Version,
    bool IsStable);

public record QuiltLoaderVersionDto(
    string Version,
    bool IsStable);

public record QuiltInstallResultDto(
    string InstallId,
    string Status,
    string? Error);

public record QuiltInstallStatusDto(
    string InstallId,
    string MinecraftVersion,
    string LoaderVersion,
    string ServerName,
    string Status,
    int Progress,
    string? CurrentStep,
    string? Error,
    string? Output,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
