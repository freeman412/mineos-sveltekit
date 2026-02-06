using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Services;

public sealed class CronSchedulerService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundJobService _jobService;
    private readonly ILogger<CronSchedulerService> _logger;
    private Timer? _timer;

    public CronSchedulerService(
        IServiceScopeFactory scopeFactory,
        IBackgroundJobService jobService,
        ILogger<CronSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _jobService = jobService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cron scheduler started");
        // Check every 60 seconds, aligned to the start of each minute
        var now = DateTimeOffset.UtcNow;
        var delayToNextMinute = 60 - now.Second;
        _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(delayToNextMinute), TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cron scheduler stopped");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private async void OnTick(object? state)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<CronJob>>();
            var jobs = await repo.ToListAsync(j => j.Enabled, CancellationToken.None);

            var now = DateTimeOffset.UtcNow;
            foreach (var job in jobs)
            {
                if (!CronExpressionMatcher.Matches(job.CronExpression, now))
                    continue;

                _logger.LogInformation("Cron trigger: {Action} for server {Server} (expression: {Expr})",
                    job.Action, job.ServerName, job.CronExpression);

                QueueCronAction(job);

                job.LastRunAt = now;
                await repo.UpdateAsync(job, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in cron scheduler tick");
        }
    }

    private void QueueCronAction(CronJob job)
    {
        switch (job.Action.ToLowerInvariant())
        {
            case "backup":
                _jobService.QueueJob("backup", job.ServerName, async (sp, progress, ct) =>
                {
                    var backupService = sp.GetRequiredService<IBackupService>();
                    progress.Report(new JobProgressDto(
                        string.Empty, "backup", job.ServerName, "running",
                        10, "Starting scheduled backup...", DateTimeOffset.UtcNow));
                    await backupService.CreateBackupAsync(job.ServerName, ct);
                    progress.Report(new JobProgressDto(
                        string.Empty, "backup", job.ServerName, "completed",
                        100, "Scheduled backup completed", DateTimeOffset.UtcNow));
                });
                break;

            case "restart":
                _jobService.QueueJob("restart", job.ServerName, async (sp, progress, ct) =>
                {
                    var serverService = sp.GetRequiredService<IServerService>();
                    progress.Report(new JobProgressDto(
                        string.Empty, "restart", job.ServerName, "running",
                        30, "Stopping server...", DateTimeOffset.UtcNow));
                    await serverService.StopServerAsync(job.ServerName, 30, ct);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    progress.Report(new JobProgressDto(
                        string.Empty, "restart", job.ServerName, "running",
                        70, "Starting server...", DateTimeOffset.UtcNow));
                    await serverService.StartServerAsync(job.ServerName, ct);
                    progress.Report(new JobProgressDto(
                        string.Empty, "restart", job.ServerName, "completed",
                        100, "Scheduled restart completed", DateTimeOffset.UtcNow));
                });
                break;

            default:
                _logger.LogWarning("Unknown cron action: {Action}", job.Action);
                break;
        }
    }
}

internal static class CronExpressionMatcher
{
    /// <summary>
    /// Matches a standard 5-field cron expression (minute hour day-of-month month day-of-week)
    /// against a given time. Supports: * (any), specific values, ranges (1-5), lists (1,3,5), steps (*/5).
    /// </summary>
    public static bool Matches(string expression, DateTimeOffset time)
    {
        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) return false;

        return FieldMatches(parts[0], time.Minute, 0, 59)
            && FieldMatches(parts[1], time.Hour, 0, 23)
            && FieldMatches(parts[2], time.Day, 1, 31)
            && FieldMatches(parts[3], time.Month, 1, 12)
            && FieldMatches(parts[4], (int)time.DayOfWeek, 0, 6);
    }

    private static bool FieldMatches(string field, int value, int min, int max)
    {
        foreach (var part in field.Split(','))
        {
            if (PartMatches(part.Trim(), value, min, max))
                return true;
        }
        return false;
    }

    private static bool PartMatches(string part, int value, int min, int max)
    {
        // Handle step: */5 or 1-10/2
        var stepParts = part.Split('/');
        var rangeStr = stepParts[0];
        var step = stepParts.Length > 1 && int.TryParse(stepParts[1], out var s) ? s : 1;

        if (rangeStr == "*")
        {
            return step == 1 || (value - min) % step == 0;
        }

        // Handle range: 1-5
        var rangeParts = rangeStr.Split('-');
        if (rangeParts.Length == 2
            && int.TryParse(rangeParts[0], out var from)
            && int.TryParse(rangeParts[1], out var to))
        {
            if (value < from || value > to) return false;
            return step == 1 || (value - from) % step == 0;
        }

        // Exact value
        return int.TryParse(rangeStr, out var exact) && exact == value;
    }
}
