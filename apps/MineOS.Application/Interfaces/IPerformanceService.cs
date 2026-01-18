using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IPerformanceService
{
    Task<PerformanceSampleDto> GetRealtimeAsync(string serverName, CancellationToken cancellationToken);
    Task<IReadOnlyList<PerformanceSampleDto>> GetHistoryAsync(
        string serverName,
        TimeSpan window,
        CancellationToken cancellationToken);
    Task RecordSampleAsync(string serverName, CancellationToken cancellationToken);
    IAsyncEnumerable<PerformanceSampleDto> StreamRealtimeAsync(
        string serverName,
        TimeSpan interval,
        CancellationToken cancellationToken);
    Task<SparkStatusDto> GetSparkStatusAsync(string serverName, CancellationToken cancellationToken);
}
