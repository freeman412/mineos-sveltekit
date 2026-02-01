using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Services;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Constants;

namespace MineOS.Infrastructure.Services;

public sealed class BackgroundJobService : IBackgroundJobService, IHostedService
{
    private readonly ILogger<BackgroundJobService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRepository<JobRecord> _jobRepo;
    private readonly IRepository<SystemNotification> _notificationRepo;
    private readonly Channel<BackgroundJob> _jobQueue;
    private readonly ConcurrentDictionary<string, JobState> _jobs;
    private readonly ConcurrentDictionary<string, ModpackInstallState> _modpackStates = new();
    private readonly Channel<ModpackJob> _modpackJobQueue;
    private Task? _executingTask;
    private Task? _modpackExecutingTask;
    private readonly CancellationTokenSource _stoppingCts = new();

    public BackgroundJobService(
        ILogger<BackgroundJobService> logger,
        IServiceScopeFactory scopeFactory,
        IRepository<JobRecord> jobRepo,
        IRepository<SystemNotification> notificationRepo)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _jobRepo = jobRepo;
        _notificationRepo = notificationRepo;
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
        };
        state.SetStartedAt(DateTimeOffset.UtcNow);

        _jobs[jobId] = state;
        _jobQueue.Writer.TryWrite(job);
        PersistJobFireAndForget(state);

        _logger.LogInformation("Queued job {JobId} ({Type}) for server {ServerName}", jobId, type, serverName);

        return jobId;
    }

    public JobStatusDto? GetJobStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
            return GetJobStatusFromDb(jobId);
        }

        var s = state.Snapshot();
        return new JobStatusDto(
            state.JobId,
            state.Type,
            state.ServerName,
            s.Status,
            s.Percentage,
            s.Message,
            s.StartedAt,
            s.CompletedAt,
            s.Error
        );
    }

    public IReadOnlyList<JobStatusDto> GetActiveJobs()
    {
        return _jobs.Values
            .Select(j => (State: j, Snap: j.Snapshot()))
            .Where(x => x.Snap.Status == JobStatus.Queued || x.Snap.Status == JobStatus.Running)
            .Select(x => new JobStatusDto(
                x.State.JobId,
                x.State.Type,
                x.State.ServerName,
                x.Snap.Status,
                x.Snap.Percentage,
                x.Snap.Message,
                x.Snap.StartedAt,
                x.Snap.CompletedAt,
                x.Snap.Error))
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
        var snap = state.Snapshot();
        yield return new JobProgressDto(
            state.JobId, state.Type, state.ServerName,
            snap.Status, snap.Percentage, snap.Message,
            DateTimeOffset.UtcNow
        );

        // Stream updates
        while (!cancellationToken.IsCancellationRequested)
        {
            snap = state.Snapshot();
            if (snap.Status == JobStatus.Completed || snap.Status == JobStatus.Failed)
            {
                yield return new JobProgressDto(
                    state.JobId, state.Type, state.ServerName,
                    snap.Status, snap.Percentage, snap.Message ?? snap.Error,
                    DateTimeOffset.UtcNow
                );
                yield break;
            }

            await Task.Delay(500, cancellationToken);

            snap = state.Snapshot();
            yield return new JobProgressDto(
                state.JobId, state.Type, state.ServerName,
                snap.Status, snap.Percentage, snap.Message,
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

        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Background job service shutdown timed out after 30 seconds");
        }
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

        state.Update("running", percentage: 0);
        PersistJobFireAndForget(state);

        var progress = new Progress<JobProgressDto>(p =>
        {
            state.UpdateProgress(p.Percentage, p.Message);
            PersistJobFireAndForget(state);
        });

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await job.Work(scope.ServiceProvider, progress, stoppingToken);

            state.Update("completed", percentage: 100, completedAt: DateTimeOffset.UtcNow);
            PersistJobFireAndForget(state);
            await CreateJobNotificationAsync(state, "completed", CancellationToken.None);

            _logger.LogInformation("Job {JobId} completed successfully", job.JobId);
        }
        catch (OperationCanceledException)
        {
            state.Update("failed", error: "Cancelled", completedAt: DateTimeOffset.UtcNow);
            PersistJobFireAndForget(state);
            await CreateJobNotificationAsync(state, "cancelled", CancellationToken.None);
            _logger.LogWarning("Job {JobId} was cancelled", job.JobId);
        }
        catch (Exception ex)
        {
            state.Update("failed", error: ex.Message, completedAt: DateTimeOffset.UtcNow);
            PersistJobFireAndForget(state);
            await CreateJobNotificationAsync(state, "failed", CancellationToken.None);

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
            await CreateModpackNotificationAsync(job.ServerName, "completed", null, CancellationToken.None);
            _logger.LogInformation("Modpack install job {JobId} completed successfully", job.JobId);
        }
        catch (OperationCanceledException)
        {
            state.MarkFailed("Installation was cancelled");
            await CreateModpackNotificationAsync(job.ServerName, "cancelled", "Installation was cancelled", CancellationToken.None);
            _logger.LogWarning("Modpack install job {JobId} was cancelled", job.JobId);
        }
        catch (Exception ex)
        {
            state.MarkFailed(ex.Message);
            await CreateModpackNotificationAsync(job.ServerName, "failed", ex.Message, CancellationToken.None);
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
        private readonly object _lock = new();
        public required string JobId { get; init; }
        public required string Type { get; init; }
        public required string ServerName { get; init; }

        private string _status = JobStatus.Queued;
        private int _percentage;
        private string? _message;
        private DateTimeOffset _startedAt;
        private DateTimeOffset? _completedAt;
        private string? _error;

        public void Update(string status, int? percentage = null, string? message = null,
            DateTimeOffset? completedAt = null, string? error = null)
        {
            lock (_lock)
            {
                _status = status;
                if (percentage.HasValue) _percentage = percentage.Value;
                if (message != null) _message = message;
                if (completedAt.HasValue) _completedAt = completedAt.Value;
                if (error != null) _error = error;
            }
        }

        public void SetStartedAt(DateTimeOffset value)
        {
            lock (_lock) { _startedAt = value; }
        }

        public void UpdateProgress(int percentage, string? message)
        {
            lock (_lock)
            {
                _percentage = percentage;
                _message = message;
            }
        }

        public (string Status, int Percentage, string? Message, DateTimeOffset StartedAt,
            DateTimeOffset? CompletedAt, string? Error) Snapshot()
        {
            lock (_lock)
            {
                return (_status, _percentage, _message, _startedAt, _completedAt, _error);
            }
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
            var record = _jobRepo.FindById(jobId);
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

    private async Task<JobRecord?> GetJobRecordAsync(string jobId, CancellationToken cancellationToken)
    {
        return await _jobRepo.FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
    }

    private async Task UpsertJobAsync(JobState state, CancellationToken cancellationToken)
    {
        var snap = state.Snapshot();

        var record = await _jobRepo.FindByIdAsync(cancellationToken, state.JobId);
        if (record == null)
        {
            record = new JobRecord
            {
                JobId = state.JobId,
                Type = state.Type,
                ServerName = state.ServerName,
                Status = snap.Status,
                Percentage = snap.Percentage,
                Message = snap.Message,
                Error = snap.Error,
                StartedAt = snap.StartedAt,
                CompletedAt = snap.CompletedAt
            };
            await _jobRepo.AddAsync(record, cancellationToken);
        }
        else
        {
            record.Status = snap.Status;
            record.Percentage = snap.Percentage;
            record.Message = snap.Message;
            record.Error = snap.Error;
            record.CompletedAt = snap.CompletedAt;
            await _jobRepo.UpdateAsync(record, cancellationToken);
        }
    }

    private async Task CreateJobNotificationAsync(JobState state, string outcome, CancellationToken cancellationToken)
    {
        var snap = state.Snapshot();
        var (type, titleSuffix) = GetNotificationType(outcome);
        var jobLabel = GetJobDisplayName(state.Type);
        var title = $"{jobLabel} {titleSuffix}";
        var message = outcome switch
        {
            "completed" => $"{jobLabel} for {state.ServerName} completed successfully.",
            "cancelled" => $"{jobLabel} for {state.ServerName} was cancelled.",
            _ => $"{jobLabel} for {state.ServerName} failed: {snap.Error ?? "Unknown error"}."
        };

        await CreateNotificationAsync(type, title, message, state.ServerName, cancellationToken);
    }

    private async Task CreateModpackNotificationAsync(
        string serverName,
        string outcome,
        string? error,
        CancellationToken cancellationToken)
    {
        var (type, titleSuffix) = GetNotificationType(outcome);
        var title = $"Modpack Install {titleSuffix}";
        var message = outcome switch
        {
            "completed" => $"Modpack install for {serverName} completed successfully.",
            "cancelled" => $"Modpack install for {serverName} was cancelled.",
            _ => $"Modpack install for {serverName} failed: {error ?? "Unknown error"}."
        };

        await CreateNotificationAsync(type, title, message, serverName, cancellationToken);
    }

    private async Task CreateNotificationAsync(
        string type,
        string title,
        string message,
        string? serverName,
        CancellationToken cancellationToken)
    {
        await _notificationRepo.AddAsync(new SystemNotification
        {
            Type = type,
            Title = title,
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow,
            ServerName = serverName,
            IsRead = false
        }, cancellationToken);
    }

    private static (string Type, string TitleSuffix) GetNotificationType(string outcome)
    {
        return outcome switch
        {
            "completed" => ("success", "Completed"),
            "cancelled" => ("warning", "Cancelled"),
            _ => ("error", "Failed")
        };
    }

    private static string GetJobDisplayName(string type)
    {
        return type.Trim().ToLowerInvariant() switch
        {
            "import" => "Server Import",
            "backup" => "Backup",
            "restore" => "Restore",
            "download" => "Download",
            "buildtools" => "BuildTools",
            "mod-install" => "Mod Install",
            "modpack-install" => "Modpack Install",
            "modrinth-modpack-install" => "Modrinth Modpack Install",
            "archive" => "Archive",
            _ => type.Replace('-', ' ')
        };
    }
}
