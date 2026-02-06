namespace MineOS.Infrastructure.Constants;

/// <summary>
/// Constant values for background job status strings.
/// Prevents hardcoding status values throughout the codebase.
/// </summary>
public static class JobStatus
{
    /// <summary>
    /// Job is waiting in the queue to be executed.
    /// </summary>
    public const string Queued = "queued";

    /// <summary>
    /// Job is currently being executed.
    /// </summary>
    public const string Running = "running";

    /// <summary>
    /// Job completed successfully.
    /// </summary>
    public const string Completed = "completed";

    /// <summary>
    /// Job failed with an error.
    /// </summary>
    public const string Failed = "failed";
}
