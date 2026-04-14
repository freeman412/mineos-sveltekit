using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class FeatureUsageTrackerTests
{
    [Fact]
    public void Increment_Tracks_Single_Feature()
    {
        var tracker = new FeatureUsageTracker();
        tracker.Increment("backups");

        var result = tracker.GetAndReset();
        Assert.Single(result);
        Assert.Equal(1, result["backups"]);
    }

    [Fact]
    public void Increment_Accumulates_Counts()
    {
        var tracker = new FeatureUsageTracker();
        tracker.Increment("backups", 3);
        tracker.Increment("backups", 2);

        var result = tracker.GetAndReset();
        Assert.Equal(5, result["backups"]);
    }

    [Fact]
    public void Increment_Tracks_Multiple_Features()
    {
        var tracker = new FeatureUsageTracker();
        tracker.Increment("backups");
        tracker.Increment("console_commands", 5);

        var result = tracker.GetAndReset();
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["backups"]);
        Assert.Equal(5, result["console_commands"]);
    }

    [Fact]
    public void GetAndReset_Clears_Counts()
    {
        var tracker = new FeatureUsageTracker();
        tracker.Increment("backups", 10);

        var first = tracker.GetAndReset();
        Assert.Equal(10, first["backups"]);

        var second = tracker.GetAndReset();
        Assert.Empty(second);
    }

    [Fact]
    public void GetAndReset_On_Empty_Returns_Empty()
    {
        var tracker = new FeatureUsageTracker();
        var result = tracker.GetAndReset();
        Assert.Empty(result);
    }

    [Fact]
    public void Concurrent_Increments_Are_Safe()
    {
        var tracker = new FeatureUsageTracker();
        const int iterations = 1000;

        Parallel.For(0, iterations, _ => tracker.Increment("counter"));

        var result = tracker.GetAndReset();
        Assert.Equal(iterations, result["counter"]);
    }
}
