// apps/MineOS.Tests/Unit/AiDiagnosisPreviewTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class AiDiagnosisPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mineos-test-" + Guid.NewGuid());

    private AiDiagnosisService Build(CrashEvent crashEvent, ServerLoaderDto? loader = null)
    {
        Directory.CreateDirectory(Path.Combine(_root, "smp", "logs"));
        Directory.CreateDirectory(Path.Combine(_root, "smp", "crash-reports"));

        var paths = new Mock<IServerPathProvider>();
        paths.Setup(p => p.GetServerPath("smp")).Returns(Path.Combine(_root, "smp"));
        paths.Setup(p => p.GetLogPath("smp")).Returns(Path.Combine(_root, "smp", "logs", "latest.log"));
        paths.Setup(p => p.GetCrashReportsPath("smp")).Returns(Path.Combine(_root, "smp", "crash-reports"));

        var events = new Mock<IRepository<CrashEvent>>();
        events.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrashEvent, bool>>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(crashEvent);

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("true");

        var players = new Mock<IPlayerService>();
        players.Setup(p => p.ListPlayersAsync("smp", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<PlayerSummaryDto>());

        var servers = new Mock<IServerService>();
        servers.Setup(s => s.DetectLoaderAsync("smp", It.IsAny<CancellationToken>()))
               .ReturnsAsync(loader ?? new ServerLoaderDto(null, null));

        return new AiDiagnosisService(
            paths.Object,
            events.Object,
            new Mock<IRepository<CrashDiagnosis>>().Object,
            new Mock<IAiCompletionService>().Object,
            settings.Object,
            players.Object,
            servers.Object,
            NullLogger<AiDiagnosisService>.Instance);
    }

    private static CrashEvent Event() => new()
    {
        Id = 7,
        ServerName = "smp",
        DetectedAt = DateTimeOffset.UtcNow,
        CrashType = "CrashReport"
    };

    [Fact]
    public async Task PreviewRedactsTheCrashReport()
    {
        var service = Build(Event());
        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "crash-reports", "crash-2026-08-29.txt"),
            "at /home/dfreeman/mods/jei-1.20.1.jar\nSteve[/192.168.1.50:52344] logged in");

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.DoesNotContain("192.168.1.50", preview.RedactedInput);
        Assert.Contains("jei-1.20.1.jar", preview.RedactedInput);
        Assert.Contains("ip-address", preview.RulesApplied);
        Assert.True(preview.ApproxCharacters > 0);
    }

    [Fact]
    public async Task PreviewFallsBackToTheLogTailWhenThereIsNoCrashReport()
    {
        var service = Build(Event());
        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "logs", "latest.log"),
            "[12:00:00] [Server thread/ERROR]: java.lang.OutOfMemoryError");

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.Contains("OutOfMemoryError", preview.RedactedInput);
    }

    [Fact]
    public async Task AnOversizedCrashReportDoesNotEvictTheDistilledLog()
    {
        var service = Build(Event());

        // Well over the 24,000-character per-section cap for the crash report.
        var report = "java.lang.OutOfMemoryError: Java heap space\n"
            + string.Join("\n", Enumerable.Range(0, 1_200)
                .Select(i => $"\tat net.minecraft.some.very.long.package.name.Class{i}.method{i}(Class{i}.java:{i})"));
        Assert.True(report.Length > 24_000);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "crash-reports", "crash-2026-08-29.txt"), report);

        var log = string.Join("\n", Enumerable.Range(0, 500)
            .Select(i => $"[12:00:00] [Server thread/INFO]: Saving chunks for level 'ServerLevel[world]' {i}")
            .Append("[12:41:07] [Server thread/INFO]: the distinctive final log line"));
        await File.WriteAllTextAsync(Path.Combine(_root, "smp", "logs", "latest.log"), log);

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.Contains("the distinctive final log line", preview.RedactedInput);
    }


    [Fact]
    public async Task IncludesTheServerTypeVersionAndLoader()
    {
        var service = Build(Event(), new ServerLoaderDto("neoforge", "20.4.190", "1.20.4"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "logs", "latest.log"),
            "[12:00:00] [Server thread/ERROR]: java.lang.OutOfMemoryError");

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.Contains("Server type: neoforge", preview.RedactedInput);
        Assert.Contains("Minecraft version: 1.20.4", preview.RedactedInput);
        Assert.Contains("Loader version: 20.4.190", preview.RedactedInput);
    }

    [Fact]
    public async Task OmitsMetadataLinesRatherThanInventingPlaceholders()
    {
        var service = Build(Event(), new ServerLoaderDto(null, null, null));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "logs", "latest.log"),
            "[12:00:00] [Server thread/ERROR]: java.lang.OutOfMemoryError");

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.DoesNotContain("Server type:", preview.RedactedInput);
        Assert.DoesNotContain("Loader version:", preview.RedactedInput);
    }

    [Fact]
    public async Task BothSectionsAtTheirCapsStillLeaveTheDistilledTailIntact()
    {
        // The existing oversized-report test pairs a huge report with a tiny log, so the total
        // never approaches MaxInputCharacters and the final backstop never binds. This one puts
        // BOTH sections at their caps at once — the case where the budgets have to reconcile.
        var service = Build(Event());

        var report = "java.lang.OutOfMemoryError: Java heap space\n"
            + string.Join("\n", Enumerable.Range(0, 1_500)
                .Select(i => $"\tat net.minecraft.some.very.long.package.name.Class{i}.method{i}(Class{i}.java:{i})"));
        Assert.True(report.Length > 24_000);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "crash-reports", "crash-2026-08-29.txt"), report);

        // Distinct problem events, so the distiller cannot collapse them into a count and its
        // output really does reach its 39,000-character cap. The distinguishing token has to be
        // alphabetic: the distiller normalises numbers to <n> when deciding what is the same
        // fault, so "machine_1" and "machine_2" would be one event.
        var log = new List<string>
        {
            "[12:00:00] [Server thread/INFO]: Starting minecraft server version 1.20.1"
        };
        for (var i = 0; i < 3_000; i++)
        {
            var id = Name(i);
            log.Add($"[12:0{i % 10}:00] [Server thread/ERROR]: Exception ticking entity thermal:machine_{id}");
            log.Add($"\tat cofh.thermal.core.Block{id}.tick(Block{id}.java:1)");
        }
        log.Add("[12:41:07] [Server thread/INFO]: the distinctive final log line");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "logs", "latest.log"), string.Join("\n", log));

        var preview = await service.PreviewAsync("smp", 7, default);

        // Both sections really were at their caps — otherwise this test proves nothing.
        Assert.True(preview.ApproxCharacters > 60_000, $"only {preview.ApproxCharacters} characters");
        Assert.Contains("the distinctive final log line", preview.RedactedInput);
        Assert.DoesNotContain("--- truncated ---", preview.RedactedInput);
    }

    [Fact]
    public async Task IgnoresACrashReportFromAnUnrelatedCrash()
    {
        // ProcessDeath and OutOfMemory produce no crash report at all. Attaching the nearest one
        // regardless of age hands the model a real stack trace about a different incident.
        var crash = DateTimeOffset.UtcNow;
        var service = Build(new CrashEvent
        {
            Id = 7, ServerName = "smp", DetectedAt = crash, CrashType = "ProcessDeath"
        });

        var stale = Path.Combine(_root, "smp", "crash-reports", "crash-2026-01-01.txt");
        await File.WriteAllTextAsync(stale, "java.lang.IllegalStateException from three months ago");
        File.SetLastWriteTimeUtc(stale, crash.UtcDateTime.AddHours(-3));

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.DoesNotContain("three months ago", preview.RedactedInput);
        Assert.Contains("no crash report was produced", preview.RedactedInput);
    }

    [Fact]
    public async Task KeepsACrashReportWrittenAtTheMomentOfTheCrash()
    {
        var crash = DateTimeOffset.UtcNow;
        var service = Build(new CrashEvent
        {
            Id = 7, ServerName = "smp", DetectedAt = crash, CrashType = "CrashReport"
        });

        var fresh = Path.Combine(_root, "smp", "crash-reports", "crash-2026-08-29.txt");
        await File.WriteAllTextAsync(fresh, "java.lang.IllegalStateException at the crash");
        File.SetLastWriteTimeUtc(fresh, crash.UtcDateTime.AddSeconds(-30));

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.Contains("at the crash", preview.RedactedInput);
    }

    [Fact]
    public async Task PrefersTheArchivedSessionLogWhenLatestLogPostDatesTheCrash()
    {
        // Watchdog auto-restart rotates latest.log away and starts a fresh one, so by the time
        // anyone clicks Diagnose, latest.log is the POST-restart session.
        var crash = DateTimeOffset.UtcNow.AddMinutes(-5);
        var service = Build(new CrashEvent
        {
            Id = 7, ServerName = "smp", DetectedAt = crash, CrashType = "ProcessDeath"
        });

        var archive = Path.Combine(_root, "smp", "logs", "2026-08-29-1.log");
        await File.WriteAllTextAsync(archive,
            "[12:00:00] [Server thread/ERROR]: the session that actually crashed");
        File.SetLastWriteTimeUtc(archive, crash.UtcDateTime.AddSeconds(5));

        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "logs", "latest.log"),
            "[12:10:00] [Server thread/INFO]: the healthy session after the restart");

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.Contains("the session that actually crashed", preview.RedactedInput);
        Assert.DoesNotContain("the healthy session after the restart", preview.RedactedInput);
        Assert.Contains("2026-08-29-1.log", preview.RedactedInput);
    }

    [Fact]
    public async Task ReadsAGzippedArchiveWithoutLoadingItWhole()
    {
        var crash = DateTimeOffset.UtcNow.AddMinutes(-5);
        var service = Build(new CrashEvent
        {
            Id = 7, ServerName = "smp", DetectedAt = crash, CrashType = "ProcessDeath"
        });

        var archive = Path.Combine(_root, "smp", "logs", "2026-08-29-2.log.gz");
        await using (var file = File.Create(archive))
        await using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Compress))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteLineAsync("[12:00:00] [Server thread/ERROR]: gzipped crash evidence");
        }
        File.SetLastWriteTimeUtc(archive, crash.UtcDateTime.AddSeconds(5));

        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "logs", "latest.log"),
            "[12:10:00] [Server thread/INFO]: the healthy session after the restart");

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.Contains("gzipped crash evidence", preview.RedactedInput);
    }

    [Fact]
    public async Task LabelsTheLogHonestlyWhenTheCrashSessionLogIsGone()
    {
        var crash = DateTimeOffset.UtcNow.AddMinutes(-5);
        var service = Build(new CrashEvent
        {
            Id = 7, ServerName = "smp", DetectedAt = crash, CrashType = "ProcessDeath"
        });

        await File.WriteAllTextAsync(
            Path.Combine(_root, "smp", "logs", "latest.log"),
            "[12:10:00] [Server thread/INFO]: the healthy session after the restart");

        var preview = await service.PreviewAsync("smp", 7, default);

        Assert.Contains("the crash's own session log was not found", preview.RedactedInput);
        Assert.Contains("the healthy session after the restart", preview.RedactedInput);
    }

    /// <summary>An alphabetic, digit-free identifier, so each event has its own signature.</summary>
    private static string Name(int value)
    {
        var text = string.Empty;
        do
        {
            text = (char)('a' + value % 26) + text;
            value /= 26;
        } while (value > 0);
        return text;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
