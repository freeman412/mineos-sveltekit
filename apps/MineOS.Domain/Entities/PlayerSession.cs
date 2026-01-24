namespace MineOS.Domain.Entities;

/// <summary>
/// Tracks individual player sessions (join to leave periods).
/// </summary>
public sealed class PlayerSession
{
    public int Id { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string PlayerUuid { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public long? DurationSeconds { get; set; }
    public string? LeaveReason { get; set; } // "disconnect", "kick", "server_stop", "timeout"
}
