using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Infrastructure.Services;

public sealed class BackgroundJobService : IBackgroundJobService, IHostedService
{
    private readonly ILogger<BackgroundJobService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<BackgroundJob> _jobQueue;
    private readonly ConcurrentDictionary<string, JobState> _jobs;
    private readonly ConcurrentDictionary<string, ModpackInstallState> _modpackStates = new();
    private readonly Channel<ModpackJob> _modpackJobQueue;
    private Task? _executingTask;
    private Task? _modpackExecutingTask;
    private readonly CancellationTokenSource _stoppingCts = new();

    public BackgroundJobService(ILogger<BackgroundJobService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _jobQueue = Channel.CreateUnbounded<BackgroundJob>();
        _modpackJobQueue = Channel.CreateUnbounded<ModpackJob>();
        _jobs = new ConcurrentDictionary<string, JobState>();
    }

    public string QueueJob(
        string type,
        string serverName,
        Func<IServiceProvider, IProgress<JobProgressDto>, CancellationToken, Task> work)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var job = new BackgroundJob(jobId, type, serverName, work);

        var state = new JobState
        {
            JobId = jobId,
            Type = type,
            ServerName = serverName,
            Status = "queued",
            Percentage = 0,
            StartedAt = DateTimeOffset.UtcNow
        };

        _jobs[jobId] = state;
        _jobQueue.Writer.TryWrite(job);
        PersistJobSync(state);

        _logger.LogInformation("Queued job {JobId} ({Type}) for server {ServerName}", jobId, type, serverName);

        return jobId;
    }

    public JobStatusDto? GetJobStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
            return GetJobStatusFromDb(jobId);
        }

        return new JobStatusDto(
            state.JobId,
            state.Type,
            state.ServerName,
            state.Status,
            state.Percentage,
            state.Message,
            state.StartedAt,
            state.CompletedAt,
            state.Error
        );
    }

    public IReadOnlyList<JobStatusDto> GetActiveJobs()
    {
        return _jobs.Values
            .Where(j => j.Status == "queued" || j.Status == "running")
            .Select(j => new JobStatusDto(
                j.JobId,
                j.Type,
                j.ServerName,
                j.Status,
                j.Percentage,
                j.Message,
                j.StartedAt,
                j.CompletedAt,
                j.Error))
            .ToList();
    }

    public IReadOnlyList<ModpackInstallProgressDto> GetActiveModpackInstalls()
    {
        return _modpackStates.Values
            .Where(s => !s.IsComplete)
            .Select(s => s.ToDto())
            .ToList();
    }

    public async IAsyncEnumerable<JobProgressDto> StreamJobProgressAsync(
        string jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
            var dbState = await GetJobRecordAsync(jobId, cancellationToken);
            if (dbState == null)
            {
                yield break;
            }

            yield return new JobProgressDto(
                dbState.JobId,
                dbState.Type,
                dbState.ServerName,
                dbState.Status,
                dbState.Percentage,
                dbState.Message ?? dbState.Error,
                DateTimeOffset.UtcNow
            );
            yield break;
        }

        // Send current status immediately
        yield return new JobProgressDto(
            state.JobId,
            state.Type,
            state.ServerName,
            state.Status,
            state.Percentage,
            state.Message,
            DateTimeOffset.UtcNow
        );

        // Stream updates
        while (!cancellationToken.IsCancellationRequested)
        {
            if (state.Status == "completed" || state.Status == "failed")
            {
                yield return new JobProgressDto(
                    state.JobId,
                    state.Type,
                    state.ServerName,
                    state.Status,
                    state.Percentage,
                    state.Message ?? state.Error,
                    DateTimeOffset.UtcNow
                );
                yield break;
            }

            await Task.Delay(500, cancellationToken);

            yield return new JobProgressDto(
                state.JobId,
                state.Type,
                state.ServerName,
                state.Status,
                state.Percentage,
                state.Message,
                DateTimeOffset.UtcNow
            );
        }
    }

    public string QueueModpackInstall(
        string serverName,
        Func<IServiceProvider, IModpackInstallState, CancellationToken, Task> work)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var state = new ModpackInstallState(jobId, serverName);
        var job = new ModpackJob(jobId, serverName, state, work);

        _modpackStates[jobId] = state;
        _modpackJobQueue.Writer.TryWrite(job);

        _logger.LogInformation("Queued modpack install job {JobId} for server {ServerName}", jobId, serverName);

        return jobId;
    }

    public ModpackInstallProgressDto? GetModpackInstallStatus(string jobId)
    {
        if (_modpackStates.TryGetValue(jobId, out var state))
        {
            return state.ToDto();
        }
        return null;
    }

    public async IAsyncEnumerable<ModpackInstallProgressDto> StreamModpackProgressAsync(
        string jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_modpackStates.TryGetValue(jobId, out var state))
        {
            yield break;
        }

        // Send current status immediately
        yield return state.ToDto();

        // Stream updates at faster interval for real-time output
        while (!cancellationToken.IsCancellationRequested && !state.IsComplete)
        {
            await Task.Delay(300, cancellationToken);
            yield return state.ToDto();
        }

        // Final state
        if (!cancellationToken.IsCancellationRequested)
        {
            yield return state.ToDto();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _executingTask = ExecuteAsync(_stoppingCts.Token);
        _modpackExecutingTask = ExecuteModpackJobsAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executingTask == null && _modpackExecutingTask == null)
        {
            return;
        }

        _stoppingCts.Cancel();

        var tasks = new List<Task>();
        if (_executingTask != null) tasks.Add(_executingTask);
        if (_modpackExecutingTask != null) tasks.Add(_modpackExecutingTask);

        await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(Timeout.Infinite, cancellationToken));
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background job service started");

        await foreach (var job in _jobQueue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessJobAsync(job, stoppingToken);
        }
    }

    private async Task ExecuteModpackJobsAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Modpack install job service started");

        await foreach (var job in _modpackJobQueue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessModpackJobAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(BackgroundJob job, CancellationToken stoppingToken)
    {
        if (!_jobs.TryGetValue(job.JobId, out var state))
        {
            return;
        }

        state.Status = "running";
        state.Percentage = 0;
        PersistJobFireAndForget(state);

        var progress = new Progress<JobProgressDto>(p =>
        {
            state.Percentage = p.Percentage;
            state.Message = p.Message;
            PersistJobFireAndForget(state);
        });

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await job.Work(scope.ServiceProvider, progress, stoppingToken);

            state.Status = "completed";
            state.Percentage = 100;
            state.CompletedAt = DateTimeOffset.UtcNow;
            PersistJobFireAndForget(state);

            _logger.LogInformation("Job {JobId} completed successfully", job.JobId);
        }
        catch (Exception ex)
        {
            state.Status = "failed";
            state.Error = ex.Message;
            state.CompletedAt = DateTimeOffset.UtcNow;
            PersistJobFireAndForget(state);

            _logger.LogError(ex, "Job {JobId} failed", job.JobId);
        }
    }

    private async Task ProcessModpackJobAsync(ModpackJob job, CancellationToken stoppingToken)
    {
        var state = job.State;
        state.SetRunning();
        state.AppendOutput($"Starting modpack installation for {job.ServerName}...");

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await job.Work(scope.ServiceProvider, state, stoppingToken);

            state.MarkCompleted();
            _logger.LogInformation("Modpack install job {JobId} completed successfully", job.JobId);
        }
        catch (OperationCanceledException)
        {
            state.MarkFailed("Installation was cancelled");
            _logger.LogWarning("Modpack install job {JobId} was cancelled", job.JobId);
        }
        catch (Exception ex)
        {
            state.MarkFailed(ex.Message);
            _logger.LogError(ex, "Modpack install job {JobId} failed", job.JobId);
        }
    }

    public void Dispose()
    {
        _stoppingCts.Cancel();
        _stoppingCts.Dispose();
    }

    private record BackgroundJob(
        string JobId,
        string Type,
        string ServerName,
        Func<IServiceProvider, IProgress<JobProgressDto>, CancellationToken, Task> Work
    );

    private record ModpackJob(
        string JobId,
        string ServerName,
        ModpackInstallState State,
        Func<IServiceProvider, IModpackInstallState, CancellationToken, Task> Work
    );

    private class JobState
    {
        public required string JobId { get; init; }
        public required string Type { get; init; }
        public required string ServerName { get; init; }
        public string Status { get; set; } = "queued";
        public int Percentage { get; set; }
        public string? Message { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? Error { get; set; }
    }

    private void PersistJobSync(JobState state)
    {
        try
        {
            UpsertJobAsync(state, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist job {JobId}", state.JobId);
        }
    }

    private void PersistJobFireAndForget(JobState state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await UpsertJobAsync(state, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist job {JobId}", state.JobId);
            }
        });
    }

    private JobStatusDto? GetJobStatusFromDb(string jobId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = db.Jobs.AsNoTracking().FirstOrDefault(j => j.JobId == jobId);
            if (record == null)
            {
                return null;
            }

            return new JobStatusDto(
                record.JobId,
                record.Type,
                record.ServerName,
                record.Status,
                record.Percentage,
                record.Message,
                record.StartedAt,
                record.CompletedAt,
                record.Error
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load job {JobId} from database", jobId);
            return null;
        }
    }

    private async Task<Domain.Entities.JobRecord?> GetJobRecordAsync(string jobId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
    }

    private async Task UpsertJobAsync(JobState state, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var startedAt = state.StartedAt.ToString("O");
        var completedAt = state.CompletedAt?.ToString("O");
        var message = (object?)state.Message ?? DBNull.Value;
        var error = (object?)state.Error ?? DBNull.Value;
        var completedAtValue = (object?)completedAt ?? DBNull.Value;

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Jobs (JobId, Type, ServerName, Status, Percentage, Message, Error, StartedAt, CompletedAt)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})
            ON CONFLICT(JobId) DO UPDATE SET
                Type = excluded.Type,
                ServerName = excluded.ServerName,
                Status = excluded.Status,
                Percentage = excluded.Percentage,
                Message = excluded.Message,
                Error = excluded.Error,
                StartedAt = excluded.StartedAt,
                CompletedAt = excluded.CompletedAt;
            """,
            state.JobId,
            state.Type,
            state.ServerName,
            state.Status,
            state.Percentage,
            message,
            error,
            startedAt,
            completedAtValue);
    }
}
