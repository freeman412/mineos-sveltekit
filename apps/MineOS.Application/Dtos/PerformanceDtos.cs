namespace MineOS.Application.Dtos;

public record PerformanceSampleDto(
    string ServerName,
    DateTimeOffset Timestamp,
    bool IsRunning,
    double CpuPercent,
    long RamUsedMb,
    long RamTotalMb,
    double? Tps,
    int PlayerCount);

public record SparkStatusDto(
    bool Installed,
    string? Mode,
    string? JarName,
    string? Version,
    int ReportCount,
    IReadOnlyList<string> Reports);
