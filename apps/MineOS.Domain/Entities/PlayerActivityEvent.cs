namespace MineOS.Domain.Entities;

/// <summary>
/// Tracks individual player activity events extracted from server logs.
/// </summary>
public sealed class PlayerActivityEvent
{
    public int Id { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string PlayerUuid { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty; // "join", "leave", "death", "achievement", "chat"
    public string? EventData { get; set; } // JSON for additional metadata (death cause, achievement name, etc.)
}
