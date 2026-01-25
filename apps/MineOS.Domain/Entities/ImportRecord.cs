namespace MineOS.Domain.Entities;

/// <summary>
/// Tracks archives that have been imported as servers.
/// </summary>
public sealed class ImportRecord
{
    public int Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }
}
