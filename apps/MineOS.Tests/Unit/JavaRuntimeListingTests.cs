using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers HostService.ParseJavaMajor, which turns a JDK's reported version or its
/// install directory name into a Java major for the Java Binary picker's labels.
///
/// The awkward case is Java 8, which reports itself as "1.8.0_502": the leading 1 is
/// the old product version, so the major is the second component. Every other release
/// since Java 9 leads with its major.
/// </summary>
public class JavaRuntimeListingTests
{
    [Theory]
    // JAVA_VERSION as written in a JDK's release file.
    [InlineData("25.0.4", 25)]
    [InlineData("21.0.12", 21)]
    [InlineData("17.0.9", 17)]
    [InlineData("1.8.0_502", 8)]
    // Directory names, used when the release file is missing or unreadable.
    [InlineData("temurin-25-jdk-arm64", 25)]
    [InlineData("temurin-21-jdk-amd64", 21)]
    [InlineData("temurin-8-jre-arm64", 8)]
    [InlineData("java-17-openjdk-amd64", 17)]
    // Nothing usable: label falls back to the directory name rather than inventing one.
    [InlineData("openjdk", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParsesJavaMajor(string? value, int? expected)
    {
        Assert.Equal(expected, HostService.ParseJavaMajor(value));
    }
}
