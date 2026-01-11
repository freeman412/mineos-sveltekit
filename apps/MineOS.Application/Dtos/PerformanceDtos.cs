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
