using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class TpsParsingTests
{
    [Theory]
    [InlineData("TPS from last 1m, 5m, 15m: 20.00, 19.98, 20.00", 20.00)]
    [InlineData("TPS from last 1m, 5m, 15m: 18.50, 19.00, 19.50", 18.50)]
    [InlineData("[09:30:00 INFO]: TPS from last 1m, 5m, 15m: 15.2, 16.0, 17.5", 15.2)]
    public void ParsePaperTps_Extracts_OneMinute_Value(string line, double expected)
    {
        var result = PerformanceService.TryParseTpsLine(line);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value, precision: 2);
    }

    [Theory]
    [InlineData("Dim 0 (overworld): Mean tick time: 12.3 ms. Mean TPS: 20.00", 20.00)]
    [InlineData("Dim 0 (overworld): Mean tick time: 45.0 ms. Mean TPS: 18.50", 18.50)]
    [InlineData("Overall: Mean tick time: 10.5 ms. Mean TPS: 19.95", 19.95)]
    public void ParseForgeTps_Extracts_MeanTps(string line, double expected)
    {
        var result = PerformanceService.TryParseTpsLine(line);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value, precision: 2);
    }

    [Theory]
    [InlineData("Server started.")]
    [InlineData("[09:30:00 INFO]: Player joined")]
    [InlineData("")]
    public void NonTpsLines_Return_Null(string line)
    {
        var result = PerformanceService.TryParseTpsLine(line);
        Assert.Null(result);
    }
}
