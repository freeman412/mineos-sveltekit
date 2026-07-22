using MineOS.Application;

namespace MineOS.Tests.Unit;

public class UpdateDetectionTests
{
    [Theory]
    [InlineData("21.1.227", "21.1.30", 1)]
    [InlineData("21.1.30", "21.1.227", -1)]
    [InlineData("0.16.0", "0.16.0", 0)]
    [InlineData("0.16.10", "0.16.9", 1)]
    [InlineData("1.21", "1.21.1", -1)]
    [InlineData("47.2.0", "47.1.99", 1)]
    [InlineData("v1.2.0", "1.2.0", 0)]
    public void CompareVersions_OrdersNumerically(string a, string b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(UpdateDetection.CompareVersions(a, b)));
    }

    [Fact]
    public void PickLatest_ReturnsHighestVersion()
    {
        Assert.Equal("0.16.10",
            UpdateDetection.PickLatest(new[] { "0.15.11", "0.16.10", "0.16.9" }));
        Assert.Null(UpdateDetection.PickLatest(Array.Empty<string>()));
    }

    [Theory]
    [InlineData("paper-1.21.1-133.jar", "1.21.1", 133)]
    [InlineData("servers/lobby/paper-1.20.4-499.jar", "1.20.4", 499)]
    [InlineData("PAPER-1.21-5.JAR", "1.21", 5)]
    public void TryParsePaperJar_ParsesVersionAndBuild(string jar, string mc, int build)
    {
        var parsed = UpdateDetection.TryParsePaperJar(jar);
        Assert.NotNull(parsed);
        Assert.Equal(mc, parsed!.Value.McVersion);
        Assert.Equal(build, parsed.Value.Build);
    }

    [Theory]
    [InlineData("paper.jar")]
    [InlineData("paper-1.21.1.jar")]
    [InlineData("spigot-1.21.1-133.jar")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParsePaperJar_RejectsUnversionedNames(string? jar)
    {
        Assert.Null(UpdateDetection.TryParsePaperJar(jar));
    }

    [Theory]
    [InlineData("1.20.1-47.2.0", "1.20.1", "47.2.0")]
    [InlineData("1.21.1-52.0.9", "1.21.1", "52.0.9")]
    public void TrySplitForgeVersion_Splits(string full, string mc, string forge)
    {
        var split = UpdateDetection.TrySplitForgeVersion(full);
        Assert.NotNull(split);
        Assert.Equal(mc, split!.Value.McVersion);
        Assert.Equal(forge, split.Value.ForgeVersion);
    }

    [Theory]
    [InlineData("47.2.0")]
    [InlineData("1.20.1")]
    [InlineData("-47.2.0")]
    [InlineData("")]
    [InlineData(null)]
    public void TrySplitForgeVersion_RejectsMalformed(string? full)
    {
        Assert.Null(UpdateDetection.TrySplitForgeVersion(full));
    }

    [Theory]
    [InlineData("21.1.227", "21.1")]
    [InlineData("20.4.80", "20.4")]
    public void NeoForgeMcLine_ExtractsLine(string version, string line)
    {
        Assert.Equal(line, UpdateDetection.NeoForgeMcLine(version));
    }

    [Theory]
    [InlineData("beta")]
    [InlineData("21")]
    [InlineData(null)]
    public void NeoForgeMcLine_RejectsMalformed(string? version)
    {
        Assert.Null(UpdateDetection.NeoForgeMcLine(version));
    }
}
