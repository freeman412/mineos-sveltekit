namespace MineOS.Domain.Entities;

public sealed class SystemNotification
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty; // info, warning, error, success
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public bool IsRead { get; set; }
    public int? RecipientUserId { get; set; } // Null means broadcast/global
    public string? ServerName { get; set; } // Null means global notification
}
