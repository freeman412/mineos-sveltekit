// apps/MineOS.Tests/Unit/AiDiagnosisCacheTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;
using MineOS.Infrastructure.Persistence.Repositories;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Integration-style cover for DiagnoseAsync itself — the caching, retry and rate-limit paths
/// that ParseResponse unit tests cannot reach. A real SQLite AppDbContext is used so the unique
/// index on (ServerName, SourceHash) is genuinely in play.
/// </summary>
public class AiDiagnosisCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mineos-test-" + Guid.NewGuid());
    private readonly string _dataSource = $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
    private readonly AppDbContext _keepAlive;

    public AiDiagnosisCacheTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "smp", "logs"));
        Directory.CreateDirectory(Path.Combine(_root, "smp", "crash-reports"));

        // The in-memory database lives only as long as one connection to it stays open.
        _keepAlive = NewContext();
        _keepAlive.Database.OpenConnection();
        _keepAlive.Database.EnsureCreated();
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_dataSource).Options);

    private sealed class Factory : IDbContextFactory<AppDbContext>
    {
        private readonly Func<AppDbContext> _create;
        public Factory(Func<AppDbContext> create) => _create = create;
        public AppDbContext CreateDbContext() => _create();
    }

    /// <summary>A provider that counts how many times it was actually billed.</summary>
    private sealed class CountingAi : IAiCompletionService
    {
        public int Calls;
        public Func<AiCompletionResult> Next { get; set; } =
            () => AiCompletionResult.Ok(
                """{"summary":"Mod conflict","classification":"mod-or-modpack","confidence":"high"}""",
                "test-model", 10, 20);

        /// <summary>Lets a test hold a call open inside the provider.</summary>
        public Func<Task> Before { get; set; } = () => Task.CompletedTask;

        public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct)
        {
            Calls++;
            await Before();
            return Next();
        }

        public Task<bool> IsConfiguredAsync(CancellationToken ct) => Task.FromResult(true);
    }

    private AiDiagnosisService Build(CountingAi ai, int cap = 20)
    {
        var paths = new Mock<IServerPathProvider>();
        paths.Setup(p => p.GetServerPath("smp")).Returns(Path.Combine(_root, "smp"));
        paths.Setup(p => p.GetLogPath("smp")).Returns(Path.Combine(_root, "smp", "logs", "latest.log"));
        paths.Setup(p => p.GetCrashReportsPath("smp")).Returns(Path.Combine(_root, "smp", "crash-reports"));

        var events = new Mock<IRepository<CrashEvent>>();
        events.Setup(r => r.FirstOrDefaultAsync(
                  It.IsAny<System.Linq.Expressions.Expression<Func<CrashEvent, bool>>>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(new CrashEvent
              {
                  Id = 7,
                  ServerName = "smp",
                  DetectedAt = DateTimeOffset.UtcNow,
                  CrashType = "CrashReport"
              });

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("true");
        settings.Setup(s => s.GetAsync(SettingsService.Keys.AiModel, It.IsAny<CancellationToken>()))
                .ReturnsAsync("test-model");
        settings.Setup(s => s.GetAsync(SettingsService.Keys.AiMaxDiagnosesPerHour, It.IsAny<CancellationToken>()))
                .ReturnsAsync(cap.ToString());

        var players = new Mock<IPlayerService>();
        players.Setup(p => p.ListPlayersAsync("smp", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<PlayerSummaryDto>());

        var servers = new Mock<IServerService>();
        servers.Setup(s => s.DetectLoaderAsync("smp", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ServerLoaderDto("forge", "47.2.0", "1.20.1"));

        return new AiDiagnosisService(
            paths.Object,
            events.Object,
            new Repository<CrashDiagnosis>(new Factory(NewContext)),
            ai,
            settings.Object,
            players.Object,
            servers.Object,
            NullLogger<AiDiagnosisService>.Instance);
    }

    private Task WriteLogAsync(string text) =>
        File.WriteAllTextAsync(Path.Combine(_root, "smp", "logs", "latest.log"), text);

    [Fact]
    public async Task ASecondDiagnosisOfTheSameCrashIsServedFromTheCache()
    {
        await WriteLogAsync("[12:00:00] [Server thread/ERROR]: java.lang.OutOfMemoryError");
        var ai = new CountingAi();
        var service = Build(ai);

        var first = await service.DiagnoseAsync("smp", 7, default);
        var second = await service.DiagnoseAsync("smp", 7, default);

        Assert.Equal(1, ai.Calls);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("complete", second.Status);
    }

    [Fact]
    public async Task ACachedFailureDoesNotSuppressARetry()
    {
        // The endpoint is down for five minutes. Without this, the failure is cached against a
        // now-stable input and the crash is permanently undiagnosable.
        await WriteLogAsync("[12:00:00] [Server thread/ERROR]: java.lang.OutOfMemoryError");
        var ai = new CountingAi
        {
            Next = () => AiCompletionResult.Fail(AiFailureReason.Transport, "The endpoint is unreachable.")
        };
        var service = Build(ai);

        var failed = await service.DiagnoseAsync("smp", 7, default);
        Assert.Equal("failed", failed.Status);

        ai.Next = () => AiCompletionResult.Ok(
            """{"summary":"Out of memory","classification":"environment","confidence":"high"}""",
            "test-model", 10, 20);

        var retried = await service.DiagnoseAsync("smp", 7, default);

        Assert.Equal(2, ai.Calls);
        Assert.Equal("complete", retried.Status);
        Assert.Equal("Out of memory", retried.Summary);

        // The stale failure was removed, not left behind for the unique index to trip over.
        await using var db = NewContext();
        Assert.Equal(1, await db.CrashDiagnoses.CountAsync(d => d.ServerName == "smp"));
    }

    [Fact]
    public async Task AMalformedModelResponseIsStoredAsFailedWithNoPartialFields()
    {
        await WriteLogAsync("[12:00:00] [Server thread/ERROR]: java.lang.OutOfMemoryError");
        var ai = new CountingAi { Next = () => AiCompletionResult.Ok("I think your server is sad.", "test-model", 1, 1) };
        var service = Build(ai);

        var result = await service.DiagnoseAsync("smp", 7, default);

        Assert.Equal("failed", result.Status);
        Assert.Null(result.Summary);
        Assert.Null(result.LikelyCause);
        Assert.Empty(result.SuggestedActions);
        Assert.Null(result.Classification);

        await using var db = NewContext();
        var row = await db.CrashDiagnoses.SingleAsync();
        Assert.Equal("failed", row.Status);
        // The audit trail of what was sent is still kept.
        Assert.Contains("OutOfMemoryError", row.RedactedInput);
    }

    [Fact]
    public async Task TheHourlyCapRefusesAFreshDiagnosis()
    {
        await WriteLogAsync("[12:00:00] [Server thread/ERROR]: first crash");
        var ai = new CountingAi();
        var service = Build(ai, cap: 1);

        await service.DiagnoseAsync("smp", 7, default);

        // Different input, so this is a genuine cache miss rather than a repeat.
        await WriteLogAsync("[12:30:00] [Server thread/ERROR]: a completely different failure");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DiagnoseAsync("smp", 7, default));

        Assert.Contains("hourly diagnosis limit", ex.Message);
        Assert.Equal(1, ai.Calls);
    }

    [Fact]
    public async Task ACacheHitDoesNotCountTowardsTheHourlyCap()
    {
        await WriteLogAsync("[12:00:00] [Server thread/ERROR]: java.lang.OutOfMemoryError");
        var ai = new CountingAi();
        var service = Build(ai, cap: 1);

        var first = await service.DiagnoseAsync("smp", 7, default);
        var second = await service.DiagnoseAsync("smp", 7, default);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, ai.Calls);
    }

    [Fact]
    public async Task ALosingConcurrentInsertReturnsTheWinningRowRatherThanFailing()
    {
        // Two POSTs for the same crash — two browser tabs is enough. Both read the cache before
        // either has written, so both call the provider and both insert; the unique index on
        // (ServerName, SourceHash) rejects the loser. That must not surface as an HTTP 500.
        await WriteLogAsync("[12:00:00] [Server thread/ERROR]: java.lang.OutOfMemoryError");

        var winnerAi = new CountingAi();
        var loserAi = new CountingAi();
        var winnerReady = new TaskCompletionSource();
        var loserEntered = new TaskCompletionSource();

        // The loser has already passed its cache read and is inside the provider call when the
        // winner commits its row — exactly the interleaving the race produces.
        loserAi.Before = async () =>
        {
            loserEntered.TrySetResult();
            await winnerReady.Task;
        };

        var loser = Build(loserAi).DiagnoseAsync("smp", 7, default);
        await loserEntered.Task;

        var winner = await Build(winnerAi).DiagnoseAsync("smp", 7, default);
        winnerReady.SetResult();

        var loserResult = await loser;

        Assert.Equal(winner.Id, loserResult.Id);
        Assert.Equal("complete", loserResult.Status);

        // One row, not two, and no exception escaped.
        await using var db = NewContext();
        Assert.Equal(1, await db.CrashDiagnoses.CountAsync());
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
