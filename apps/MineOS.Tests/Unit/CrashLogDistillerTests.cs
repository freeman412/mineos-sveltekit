// apps/MineOS.Tests/Unit/CrashLogDistillerTests.cs
using MineOS.Application.Dtos;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class CrashLogDistillerTests
{
    private static readonly LogDistillerOptions Default = new();

    private static string Distill(IEnumerable<string> lines, LogDistillerOptions? options = null) =>
        CrashLogDistiller.Distill(lines, options ?? Default).Text;

    private static IEnumerable<string> Noise(int count, string thread = "Server thread") =>
        Enumerable.Range(0, count).Select(i => $"[12:00:00] [{thread}/INFO]: Saving chunks for level 'ServerLevel[world]' {i}");

    [Fact]
    public void KeepsTheSessionHeaderThatATailCouldNeverReach()
    {
        var lines = new[]
        {
            "[11:00:00] [main/INFO]: Loading 412 mods",
            "[11:00:01] [main/INFO]: Forge mod loading, version 47.2.0"
        }.Concat(Noise(50_000));

        var result = Distill(lines);

        Assert.Contains("Loading 412 mods", result);
        Assert.Contains("47.2.0", result);
    }

    [Fact]
    public void KeepsAnEarlyErrorThatATailWouldMiss()
    {
        var lines = new[] { "[11:58:02] [main/ERROR]: Mod 'thermal' failed capability registration" }
            .Concat(Noise(50_000));

        Assert.Contains("thermal", Distill(lines));
    }

    [Fact]
    public void CollapsesRepeatedIdenticalErrorsIntoOneEntryWithACount()
    {
        var burst = Enumerable.Range(0, 5_000)
            .SelectMany(_ => new[]
            {
                "[12:04:31] [Server thread/ERROR]: Exception ticking entity",
                "\tat net.minecraft.world.entity.Mob.tick(Mob.java:412)"
            });

        var result = Distill(burst);

        Assert.Contains("Exception ticking entity", result);
        Assert.Contains("5,000", result.Replace("5000", "5,000"));
        // The stack must survive deduplication, not be collapsed away with the repeats.
        Assert.Contains("Mob.java:412", result);
    }

    [Fact]
    public void GroupsNearIdenticalErrorsThatDifferOnlyByVolatileIdentifiers()
    {
        var lines = Enumerable.Range(1, 800)
            .Select(i => $"[12:04:31] [Server thread/ERROR]: Entity {i} at (12.{i}, 64.0, -9.{i}) could not be ticked");

        var result = Distill(lines);

        // 800 distinct entity ids, one underlying fault.
        Assert.Single(result.Split("could not be ticked").Skip(1));
    }

    [Fact]
    public void AlwaysKeepsTheLastLinesVerbatim()
    {
        var lines = Noise(50_000).Append("[12:41:07] [Server thread/INFO]: the very last line");

        Assert.Contains("the very last line", Distill(lines));
    }

    [Fact]
    public void MarksOmissionsSoTheModelDoesNotReasonFromAbsence()
    {
        var result = Distill(Noise(50_000));

        Assert.Contains("omitted", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaysWithinTheOutputBudget()
    {
        var options = new LogDistillerOptions { MaxOutputCharacters = 4_000 };
        var lines = Enumerable.Range(0, 20_000)
            .Select(i => $"[12:00:00] [Server thread/ERROR]: distinct failure number {i} in subsystem {i}");

        var result = Distill(lines, options);

        Assert.True(result.Length <= 4_000, $"output was {result.Length} characters, budget was 4000");
    }

    [Fact]
    public void PrefersFatalOverWarnWhenTheBudgetIsTight()
    {
        var options = new LogDistillerOptions { MaxOutputCharacters = 1_200, HeaderLines = 0, TailLines = 2 };
        var lines = Enumerable.Range(0, 500)
            .Select(i => $"[12:00:00] [Server thread/WARN]: noisy warning {i}")
            .Append("[12:30:00] [Server thread/FATAL]: the actual fatal error")
            .ToList();

        var result = Distill(lines, options);

        Assert.Contains("the actual fatal error", result);
    }

    [Fact]
    public void ReportsWhatItScanned()
    {
        var stats = CrashLogDistiller.Distill(Noise(1_000), Default).Stats;

        Assert.Equal(1_000, stats.LinesScanned);
        Assert.True(stats.LinesOmitted > 0);
    }

    [Fact]
    public void KeepsTheEndOfAnOversizedLogRatherThanTheBeginning()
    {
        var options = new LogDistillerOptions { LandmarkScanByteLimit = 4_000 };
        var lines = Enumerable.Range(0, 5_000)
            .Select(i => $"[12:00:00] [Server thread/INFO]: early filler line {i}")
            .Append("[12:41:07] [Server thread/FATAL]: the crash at the very end");

        var result = CrashLogDistiller.Distill(lines, options);

        Assert.Contains("the crash at the very end", result.Text);
    }

    [Fact]
    public void KeepsTheSessionHeaderEvenOnAnOversizedLog()
    {
        var options = new LogDistillerOptions { LandmarkScanByteLimit = 500 };
        var lines = new[] { "[11:00:00] [main/INFO]: Loading 412 mods" }
            .Concat(Enumerable.Range(0, 5_000)
                .Select(i => $"[12:00:00] [Server thread/INFO]: filler {i}"));

        Assert.Contains("Loading 412 mods", CrashLogDistiller.Distill(lines, options).Text);
    }

    [Fact]
    public void KeepsErrorsApartWhenTheyDifferOnlyByAModVersion()
    {
        var lines = new[]
        {
            "[12:00:00] [main/ERROR]: Mod thermal 1.20.1-10.2.0 failed to load",
            "[12:00:01] [main/ERROR]: Mod thermal 1.20.1-10.3.0 failed to load"
        };

        var result = CrashLogDistiller.Distill(lines, Default);

        // Two different versions of the same mod are two different faults, not one repeated one.
        Assert.Equal(2, result.Stats.DistinctEvents);
        Assert.Contains("10.2.0", result.Text);
        Assert.Contains("10.3.0", result.Text);
    }

    [Fact]
    public void HandlesAnEmptyLogWithoutThrowing()
    {
        var result = CrashLogDistiller.Distill(Array.Empty<string>(), Default);

        Assert.Equal(0, result.Stats.LinesScanned);
        Assert.NotNull(result.Text);
    }
}
