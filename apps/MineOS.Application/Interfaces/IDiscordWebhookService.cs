namespace MineOS.Application.Interfaces;

public enum DiscordEventLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// A server lifecycle event destined for the configured Discord webhook.
/// </summary>
public sealed record DiscordEvent(
    string Title,
    string Message,
    DiscordEventLevel Level,
    string? ServerName,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// Map a SystemNotification type string (info/warning/error/success) to a level.
    /// </summary>
    public static DiscordEventLevel LevelFromNotificationType(string? type) =>
        type?.ToLowerInvariant() switch
        {
            "success" => DiscordEventLevel.Success,
            "warning" => DiscordEventLevel.Warning,
            "error" => DiscordEventLevel.Error,
            _ => DiscordEventLevel.Info
        };
}

/// <summary>
/// Fire-and-forget dispatch of server events to a Discord webhook.
/// </summary>
public interface IDiscordWebhookService
{
    /// <summary>
    /// Queue an event for delivery. Never blocks and never throws; the event is
    /// silently dropped when no webhook URL is configured or the queue is full.
    /// </summary>
    void QueueEvent(DiscordEvent evt);
}
