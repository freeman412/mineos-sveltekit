using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Services;
using Moq;

namespace MineOS.Tests.Unit;

/// <summary>
/// Job rows are written fire-and-forget: a status change, every progress tick and
/// the completion write each spawn their own unawaited persist. They therefore run
/// concurrently for a single job, and the write is a check-then-act — find the row,
/// insert it if it is missing. Two writers that both find nothing both insert, and
/// SQLite rejects the second with "UNIQUE constraint failed: Jobs.JobId".
/// </summary>
public class JobPersistenceConcurrencyTests
{
    /// <summary>
    /// Stands in for the Jobs table, with SQLite's primary key behaviour. The delay in
    /// FindByIdAsync widens the check-then-act window so the race is deterministic
    /// rather than a coin flip — without serialization every run must fail.
    /// </summary>
    private sealed class FakeJobRepo : IRepository<JobRecord>
    {
        private readonly ConcurrentDictionary<string, JobRecord> _rows = new();
        public int DuplicateInsertAttempts;
        public int Inserts;

        public Task<JobRecord?> FindByIdAsync(CancellationToken ct, params object[] keyValues)
        {
            var id = (string)keyValues[0];
            return Task.Run(async () =>
            {
                await Task.Delay(15, ct);
                return _rows.TryGetValue(id, out var row) ? row : null;
            }, ct);
        }

        public Task AddAsync(JobRecord entity, CancellationToken ct)
        {
            if (!_rows.TryAdd(entity.JobId, entity))
            {
                Interlocked.Increment(ref DuplicateInsertAttempts);
                throw new InvalidOperationException(
                    "SQLite Error 19: 'UNIQUE constraint failed: Jobs.JobId'.");
            }
            Interlocked.Increment(ref Inserts);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(JobRecord entity, CancellationToken ct) => Task.CompletedTask;

        public JobRecord? FindById(params object[] keyValues) => throw new NotSupportedException();
        public Task<JobRecord?> FirstOrDefaultAsync(
            System.Linq.Expressions.Expression<Func<JobRecord, bool>> predicate, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<List<JobRecord>> ToListAsync(
            System.Linq.Expressions.Expression<Func<JobRecord, bool>> predicate, CancellationToken ct)
            => throw new NotSupportedException();
        public Task AddRangeAsync(IEnumerable<JobRecord> entities, CancellationToken ct)
            => throw new NotSupportedException();
        public Task RemoveAsync(JobRecord entity, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveWhereAsync(
            System.Linq.Expressions.Expression<Func<JobRecord, bool>> predicate, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static BackgroundJobService CreateService(FakeJobRepo repo)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new BackgroundJobService(
            NullLogger<BackgroundJobService>.Instance,
            services.GetRequiredService<IServiceScopeFactory>(),
            repo,
            Mock.Of<IRepository<SystemNotification>>(),
            Mock.Of<IDiscordWebhookService>());
    }

    [Fact]
    public async Task Rapid_Progress_Reports_Insert_The_Job_Row_Exactly_Once()
    {
        var repo = new FakeJobRepo();
        var service = CreateService(repo);
        await service.StartAsync(CancellationToken.None);

        try
        {
            // Each Report spawns another unawaited persist while the first is still
            // inside FindByIdAsync — the exact overlap the live system produces.
            string? queuedId = null;
            var jobId = service.QueueJob("backup", "alpha", async (_, progress, _) =>
            {
                for (var i = 1; i <= 10; i++)
                {
                    progress.Report(new JobProgressDto(
                        queuedId!, "backup", "alpha", "running", i * 10, $"step {i}",
                        DateTimeOffset.UtcNow));
                }
                await Task.Delay(200);
            });
            queuedId = jobId;

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline
                   && service.GetJobStatus(jobId)?.Status is not ("completed" or "failed"))
            {
                await Task.Delay(50);
            }

            // Give the trailing fire-and-forget persists time to land.
            await Task.Delay(500);

            Assert.Equal(0, repo.DuplicateInsertAttempts);
            Assert.Equal(1, repo.Inserts);
        }
        finally
        {
            // StopAsync awaits the queue loops, which surface the cancellation that
            // stopped them. Not what this test is about.
            try { await service.StopAsync(CancellationToken.None); }
            catch (OperationCanceledException) { }
        }
    }
}
