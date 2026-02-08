namespace MineOS.Application.Dtos;

public record FabricGameVersionDto(
    string Version,
    bool IsStable);

public record FabricLoaderVersionDto(
    string Version,
    bool IsStable);

public record FabricInstallResultDto(
    string InstallId,
    string Status,
    string? Error);

public record FabricInstallStatusDto(
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
