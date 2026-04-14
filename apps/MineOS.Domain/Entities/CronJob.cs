namespace MineOS.Domain.Entities;

public sealed class CronJob
{
    public int Id { get; set; }
    public required string ServerName { get; set; }
    public required string CronExpression { get; set; }
    public required string Action { get; set; }
    public string? Message { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
}
