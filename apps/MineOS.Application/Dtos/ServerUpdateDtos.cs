namespace MineOS.Application.Dtos;

/// <summary>
/// Detection result for a server's software updates (issue #83).
/// "Build" offers stay on the installed Minecraft version; "jump" offers move
/// to a newer Minecraft version and are always an explicit user choice.
/// </summary>
public record ServerUpdateStatusDto(
    bool Supported,
    string? Reason,
    string Mode,
    string? Family,
    bool UpdateAvailable,
    string? CurrentVersion,
    int? CurrentBuild,
    string? LatestBuildVersion,
    int? LatestBuildNumber,
    string? LatestBuildProfileId,
    bool JumpAvailable,
    string? JumpVersion,
    string? JumpProfileId,
    string? IgnoredUpdateKey);

public record SetUpdateModeRequest(string Mode);

public record ApplyUpdateRequest(string ProfileId);

public record ApplyUpdateResultDto(
    string AppliedProfileId,
    string? PreviousJar,
    string NewJar);
