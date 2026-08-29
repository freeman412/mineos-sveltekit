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

    private AiDiagnosisService Build(CrashEvent crashEvent)
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

        return new AiDiagnosisService(
            paths.Object,
            events.Object,
            new Mock<IRepository<CrashDiagnosis>>().Object,
            new Mock<IAiCompletionService>().Object,
            settings.Object,
            players.Object,
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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
