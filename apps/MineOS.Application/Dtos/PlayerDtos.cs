namespace MineOS.Application.Dtos;

public record PlayerSummaryDto(
    string Uuid,
    string Name,
    bool Whitelisted,
    bool IsOp,
    int? OpLevel,
    bool? OpBypassPlayerLimit,
    bool Banned,
    string? BanReason,
    DateTimeOffset? BanExpiresAt,
    DateTimeOffset? LastSeen,
    long? PlayTimeSeconds);

public record PlayerStatsDto(
    string Uuid,
    string RawJson,
    DateTimeOffset? LastModified);

/// <summary>
/// Player profile from Mojang API lookup.
/// </summary>
public record MojangProfileDto(
    string Uuid,
    string Name,
    string AvatarUrl);
