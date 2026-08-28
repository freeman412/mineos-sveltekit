using MineOS.Application;

namespace MineOS.Tests.Unit;

public class CronActionsTests
{
    [Theory]
    [InlineData("backup")]
    [InlineData("restart")]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("STOP")]
    [InlineData(" start ")]
    public void IsValid_Accepts_Known_Actions(string action)
    {
        Assert.True(CronActions.IsValid(action));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("kill")]
    [InlineData("delete")]
    [InlineData("backup;rm -rf /")]
    public void IsValid_Rejects_Unknown_Actions(string? action)
    {
        Assert.False(CronActions.IsValid(action));
    }

    [Fact]
    public void All_Contains_The_Four_Supported_Actions()
    {
        Assert.Equal(new[] { "backup", "restart", "start", "stop" }, CronActions.All);
    }
}
