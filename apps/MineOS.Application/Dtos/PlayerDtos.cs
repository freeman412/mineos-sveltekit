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

/// <summary>
/// Represents a player activity event (join, leave, death, etc.).
/// </summary>
public record PlayerActivityEventDto(
    int Id,
    string ServerName,
    string PlayerUuid,
    string PlayerName,
    DateTimeOffset Timestamp,
    string EventType,
    string? EventData);

/// <summary>
/// Represents a player session (join to leave period).
/// </summary>
public record PlayerSessionDto(
    int Id,
    string ServerName,
    string PlayerUuid,
    string PlayerName,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LeftAt,
    long? DurationSeconds,
    string? LeaveReason);

/// <summary>
/// Player activity statistics summary.
/// </summary>
public record PlayerActivityStatsDto(
    string PlayerUuid,
    string PlayerName,
    int TotalSessions,
    long TotalPlayTimeSeconds,
    long AverageSessionSeconds,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    int JoinCount,
    int LeaveCount,
    int DeathCount);
