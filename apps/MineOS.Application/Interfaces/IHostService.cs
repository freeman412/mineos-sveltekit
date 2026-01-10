using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IHostService
{
    Task<HostMetricsDto> GetMetricsAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<HostMetricsDto> StreamMetricsAsync(TimeSpan interval, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServerSummaryDto>> GetServersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ArchiveEntryDto>> GetImportsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetLocalesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HostUserDto>> GetUsersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HostGroupDto>> GetGroupsAsync(CancellationToken cancellationToken);
}
