namespace MineOS.Application.Dtos;

public record ModrinthSearchResultDto(
    int Index,
    int PageSize,
    int TotalHits,
    IReadOnlyList<ModrinthProjectHitDto> Results);

public record ModrinthProjectHitDto(
    string ProjectId,
    string Slug,
    string Title,
    string Description,
    string? IconUrl,
    int Downloads,
    IReadOnlyList<string> Versions,
    IReadOnlyList<string> Categories);

public record ModrinthProjectDto(
    string Id,
    string Slug,
    string Title,
    string Description,
    string? Body,
    string ProjectType,
    int Downloads,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    string? IconUrl,
    string? ClientSide,
    string? ServerSide);

public record ModrinthVersionDto(
    string Id,
    string ProjectId,
    string Name,
    string VersionNumber,
    DateTimeOffset DatePublished,
    int Downloads,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<ModrinthVersionFileDto> Files,
    IReadOnlyList<ModrinthDependencyDto> Dependencies);

public record ModrinthVersionFileDto(
    string Url,
    string FileName,
    long Size,
    bool Primary);

public record ModrinthDependencyDto(
    string? ProjectId,
    string? VersionId,
    string DependencyType);
