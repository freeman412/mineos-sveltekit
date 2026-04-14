using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface ICronService
{
    Task<IEnumerable<CronJobDto>> ListAsync(string serverName, CancellationToken ct);
    Task<CronJobDto> CreateAsync(string serverName, CreateCronRequest request, CancellationToken ct);
    Task<CronJobDto?> UpdateAsync(string serverName, string hash, UpdateCronRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(string serverName, string hash, CancellationToken ct);
}
