using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Services;

public sealed class CronService : ICronService
{
    private readonly IRepository<CronJob> _repo;
    private readonly ILogger<CronService> _logger;

    public CronService(IRepository<CronJob> repo, ILogger<CronService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<IEnumerable<CronJobDto>> ListAsync(string serverName, CancellationToken ct)
    {
        var jobs = await _repo.ToListAsync(j => j.ServerName == serverName, ct);
        return jobs.Select(ToDto);
    }

    public async Task<CronJobDto> CreateAsync(string serverName, CreateCronRequest request, CancellationToken ct)
    {
        var entity = new CronJob
        {
            ServerName = serverName,
            CronExpression = request.Source,
            Action = request.Action,
            Message = request.Msg,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repo.AddAsync(entity, ct);
        _logger.LogInformation("Created cron job {Id} for server {Server}: {Expression} -> {Action}",
            entity.Id, serverName, request.Source, request.Action);

        return ToDto(entity);
    }

    public async Task<CronJobDto?> UpdateAsync(string serverName, string hash, UpdateCronRequest request, CancellationToken ct)
    {
        var jobs = await _repo.ToListAsync(j => j.ServerName == serverName, ct);
        var entity = jobs.FirstOrDefault(j => ComputeHash(j) == hash);
        if (entity is null) return null;

        entity.Enabled = request.Enabled;
        await _repo.UpdateAsync(entity, ct);
        _logger.LogInformation("Updated cron job {Hash} for server {Server}: Enabled={Enabled}",
            hash, serverName, request.Enabled);

        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(string serverName, string hash, CancellationToken ct)
    {
        var jobs = await _repo.ToListAsync(j => j.ServerName == serverName, ct);
        var entity = jobs.FirstOrDefault(j => ComputeHash(j) == hash);
        if (entity is null) return false;

        await _repo.RemoveAsync(entity, ct);
        _logger.LogInformation("Deleted cron job {Hash} for server {Server}", hash, serverName);
        return true;
    }

    internal static string ComputeHash(CronJob job)
    {
        var input = $"{job.Id}:{job.ServerName}:{job.CronExpression}:{job.Action}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..12];
    }

    private static CronJobDto ToDto(CronJob job) =>
        new(ComputeHash(job), job.CronExpression, job.Action, job.Message, job.Enabled);
}
