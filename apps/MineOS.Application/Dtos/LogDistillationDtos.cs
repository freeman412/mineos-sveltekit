// apps/MineOS.Application/Dtos/LogDistillationDtos.cs
namespace MineOS.Application.Dtos;

public sealed record LogDistillerOptions
{
    public int MaxOutputCharacters { get; init; } = 48_000;
    public int HeaderLines { get; init; } = 60;
    public int TailLines { get; init; } = 300;
    public int MaxStackFrames { get; init; } = 12;
    public int MaxDistinctEvents { get; init; } = 2_000;
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
