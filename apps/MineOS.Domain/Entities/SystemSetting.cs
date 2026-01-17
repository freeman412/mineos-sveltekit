namespace MineOS.Domain.Entities;

public sealed class SystemSetting
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }
    public string? Description { get; set; }
    public bool IsSecret { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
