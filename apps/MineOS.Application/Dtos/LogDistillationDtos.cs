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
    /// Byte budget for the *optional* scanning work (session-header capture and landmark
    /// matching). Once the scan passes this many bytes those are switched off and only the tail
    /// ring buffer and the event dictionary keep being filled — both of which are already
    /// O(1) in the file size. The scan itself never stops early: on an oversized log the end of
    /// the file is where the crash is, and stopping at the front would diagnose the server's
    /// startup instead of its crash.
    /// </summary>
    public long MaxScanBytes { get; init; } = 512L * 1024 * 1024;
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
