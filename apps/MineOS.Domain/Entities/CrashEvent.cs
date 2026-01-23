namespace MineOS.Domain.Entities;

public sealed class CrashEvent
{
    public int Id { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
    public string CrashType { get; set; } = string.Empty; // ProcessDeath, CrashReport, OutOfMemory, Timeout
    public string? CrashDetails { get; set; }
    public bool AutoRestartAttempted { get; set; }
    public bool AutoRestartSucceeded { get; set; }
    public DateTimeOffset? RestartAttemptedAt { get; set; }
}
