namespace MineOS.Application.Dtos;

public record DiskMetricsDto(long AvailableBytes, long FreeBytes, long TotalBytes);

public record HostMetricsDto(long UptimeSeconds, long FreeMemBytes, double[] LoadAvg, DiskMetricsDto Disk);

public record ServerSummaryDto(
    string Name,
    bool Up,
    string? Profile,
    int? Port,
    int? PlayersOnline,
    int? PlayersMax);

public record ProfileDto(
    string Id,
    string Group,
    string Type,
    string Version,
    string ReleaseTime,
    string Url,
    string Filename,
    bool Downloaded,
    object? Progress);

public record ArchiveEntryDto(DateTimeOffset Time, long Size, string Filename);

public record IncrementEntryDto(DateTimeOffset Time, string Step, long? Size, long? CumulativeSize);

public record HostUserDto(string Username, int Uid, int Gid, string Home);

public record HostGroupDto(string GroupName, int Gid);
