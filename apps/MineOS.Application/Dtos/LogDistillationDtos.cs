// apps/MineOS.Application/Dtos/LogDistillationDtos.cs
namespace MineOS.Application.Dtos;

public sealed record LogDistillerOptions
{
    public int MaxOutputCharacters { get; init; } = 48_000;
    public int HeaderLines { get; init; } = 60;
    public int TailLines { get; init; } = 300;
    public int MaxStackFrames { get; init; } = 12;
    public int MaxDistinctEvents { get; init; } = 2_000;

    /// <summary>
    /// Past this many bytes, landmark matching is disabled to bound the per-line cost of the
    /// scan; scanning always continues to the end of the log. Landmark matching is the only
    /// genuinely per-line optional work — the session header is bounded by
    /// <see cref="HeaderLines"/> in total and is always captured, and the tail ring buffer and
    /// the event dictionary are both O(1) in the file size. Stopping the scan early was never an
    /// option: on an oversized log the crash is at the END, and quitting at the front would
    /// diagnose the server's startup instead of the thing that killed it.
    /// </summary>
    public long LandmarkScanByteLimit { get; init; } = 512L * 1024 * 1024;
}

public sealed record LogDistillationStats(
    long LinesScanned,
    long BytesScanned,
    bool ScanTruncated,
    int DistinctEvents,
    long EventOccurrences,
    int EventsOmitted,
    long LinesOmitted);

public sealed record LogDistillation(string Text, LogDistillationStats Stats);
