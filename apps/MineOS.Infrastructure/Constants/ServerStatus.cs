namespace MineOS.Infrastructure.Constants;

/// <summary>
/// Constant values for server status strings.
/// Prevents hardcoding status values throughout the codebase.
/// </summary>
public static class ServerStatus
{
    /// <summary>
    /// Server is currently running with an active process.
    /// </summary>
    public const string Running = "running";

    /// <summary>
    /// Server is not running (no active process).
    /// </summary>
    public const string Stopped = "stopped";
}
