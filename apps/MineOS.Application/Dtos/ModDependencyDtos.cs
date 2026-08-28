namespace MineOS.Application.Dtos;

/// <summary>
/// What installing a mod would actually do, worked out before anything is written.
///
/// The plan exists so the user sees the whole set first: a single "install" click
/// can pull in several mods, and a server that starts missing a hard dependency
/// does not warn — it fails to boot.
/// </summary>
public record ModInstallPlanDto(
    string VersionId,
    string ProjectId,
    string Name,
    string FileName,
    // Hard dependencies that will be installed alongside it, in install order.
    IReadOnlyList<ModDependencyItemDto> Required,
    // Offered, not installed, unless the caller names them explicitly.
    IReadOnlyList<ModDependencyItemDto> Optional,
    // Dependencies this server already has. Listed so the plan explains itself
    // rather than silently showing a shorter list than the mod's page implies.
    IReadOnlyList<ModDependencyItemDto> AlreadyInstalled,
    // Dependencies that could not be resolved for this loader/Minecraft version.
    // Non-empty means installing would leave the server unable to start, so the
    // install refuses rather than proceeding part-way.
    IReadOnlyList<string> Problems);

public record ModDependencyItemDto(
    string ProjectId,
    string? VersionId,
    string Name,
    string? FileName,
    // "required" or "optional", as Modrinth reports it.
    string DependencyType);

/// <summary>What an install actually wrote.</summary>
public record ModInstallResultDto(
    IReadOnlyList<string> InstalledFiles,
    IReadOnlyList<string> SkippedAlreadyInstalled);
