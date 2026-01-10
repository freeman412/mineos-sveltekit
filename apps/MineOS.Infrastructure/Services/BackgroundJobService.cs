using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public sealed class BackgroundJobService : IBackgroundJobService, IHostedService
{
    private readonly ILogger<BackgroundJobService> _logger;
    private readonly Channel<BackgroundJob> _jobQueue;
    private readonly ConcurrentDictionary<string, JobState> _jobs;
    private Task? _executingTask;
    private readonly CancellationTokenSource _stoppingCts = new();

    public BackgroundJobService(ILogger<BackgroundJobService> logger)
    {
        _logger = logger;
        _jobQueue = Channel.CreateUnbounded<BackgroundJob>();
        _jobs = new ConcurrentDictionary<string, JobState>();
    }

    public string QueueJob(string type, string serverName, Func<IProgress<JobProgressDto>, CancellationToken, Task> work)
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

        _logger.LogInformation("Queued job {JobId} ({Type}) for server {ServerName}", jobId, type, serverName);

        return jobId;
    }

    public JobStatusDto? GetJobStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
            return null;
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

    public async IAsyncEnumerable<JobProgressDto> StreamJobProgressAsync(
        string jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _executingTask = ExecuteAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executingTask == null)
        {
            return;
        }

        _stoppingCts.Cancel();

        await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background job service started");

        await foreach (var job in _jobQueue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessJobAsync(job, stoppingToken);
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

        var progress = new Progress<JobProgressDto>(p =>
        {
            state.Percentage = p.Percentage;
            state.Message = p.Message;
        });

        try
        {
            await job.Work(progress, stoppingToken);

            state.Status = "completed";
            state.Percentage = 100;
            state.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("Job {JobId} completed successfully", job.JobId);
        }
        catch (Exception ex)
        {
            state.Status = "failed";
            state.Error = ex.Message;
            state.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogError(ex, "Job {JobId} failed", job.JobId);
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
        Func<IProgress<JobProgressDto>, CancellationToken, Task> Work
    );

    private class JobState
    {
        public required string JobId { get; init; }
        public required string Type { get; init; }
        public required string ServerName { get; init; }
        public string Status { get; set; } = "queued";
        public int Percentage { get; set; }
        public string? Message { get; set; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? Error { get; set; }
    }
}
