namespace MineOS.Application.Dtos;

public record WorldDto(
    string Name,
    string Type,
    long SizeBytes,
    DateTimeOffset? LastModified);

public record WorldInfoDto(
    string Name,
    string Type,
    long SizeBytes,
    string? Seed,
    string? LevelName,
    string? GameMode,
    string? Difficulty,
    bool? Hardcore,
    DateTimeOffset? LastModified,
    int FileCount,
    int DirectoryCount);
