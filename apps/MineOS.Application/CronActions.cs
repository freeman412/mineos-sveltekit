namespace MineOS.Application;

/// <summary>
/// The canonical set of cron job actions. The scheduler dispatch, API
/// validation, and web UI must all agree on this list.
/// </summary>
public static class CronActions
{
    public const string Backup = "backup";
    public const string Restart = "restart";
    public const string Start = "start";
    public const string Stop = "stop";

    public static readonly IReadOnlyList<string> All = new[] { Backup, Restart, Start, Stop };

    public static bool IsValid(string? action) =>
        action is not null && All.Contains(action.Trim().ToLowerInvariant());
}
