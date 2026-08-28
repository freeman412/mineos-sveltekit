using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers ProcessManager.TryGetScreenSessionName, which answers "which MineOS server is
/// this screen process running?" from a process's /proc cmdline arguments.
///
/// This exists because the previous implementation joined the NUL-separated arguments
/// with spaces and matched "mc-([^\s]+)" over the result, which truncates at the first
/// space. A server named "Server Loco" was recorded under "Server", so its own name
/// never appeared in the process table: MineOS reported it stopped while it ran, and
/// stop/kill looked for a screen PID filed under a server that does not exist.
/// </summary>
public class ScreenSessionNameTests
{
    [Fact]
    public void ReadsASessionNameContainingSpaces()
    {
        // The regression: everything after the first space used to be discarded.
        var args = new[] { "SCREEN", "-dmS", "mc-Server Loco", "bash", "-lc", "exec java -jar paper.jar" };
        Assert.Equal("Server Loco", ProcessManager.TryGetScreenSessionName(args));
    }

    [Theory]
    [InlineData("mc-serverloco", "serverloco")]
    [InlineData("mc-Server Loco", "Server Loco")]
    [InlineData("mc-my server with many spaces", "my server with many spaces")]
    [InlineData("mc-hub", "hub")]
    public void ReadsTheSessionNameVerbatim(string session, string expected)
    {
        var args = new[] { "SCREEN", "-dmS", session, "bash" };
        Assert.Equal(expected, ProcessManager.TryGetScreenSessionName(args));
    }

    [Theory]
    // A detached session renames itself to SCREEN; both spellings and a full path count.
    [InlineData("SCREEN")]
    [InlineData("screen")]
    [InlineData("/usr/bin/screen")]
    public void AcceptsHoweverScreenNamesItself(string program)
    {
        var args = new[] { program, "-dmS", "mc-hub", "bash" };
        Assert.Equal("hub", ProcessManager.TryGetScreenSessionName(args));
    }

    [Theory]
    // -S on its own, and combined flag groups ending in S.
    [InlineData("-S")]
    [InlineData("-dmS")]
    [InlineData("-dS")]
    public void FindsTheNameAfterAnyFlagGroupEndingInS(string flag)
    {
        var args = new[] { "screen", flag, "mc-hub", "bash" };
        Assert.Equal("hub", ProcessManager.TryGetScreenSessionName(args));
    }

    [Fact]
    public void IgnoresTheSuProcessThatLaunchedScreen()
    {
        // su carries the entire command in one -c argument. Scanning it as though the
        // pieces were separate arguments would file a second, bogus PID for the server.
        var args = new[]
        {
            "/bin/su", "-", "minecraft", "-c",
            "screen -dmS 'mc-Server Loco' bash -lc 'exec java -jar paper.jar'"
        };
        Assert.Null(ProcessManager.TryGetScreenSessionName(args));
    }

    [Fact]
    public void IgnoresScreenSessionsThatAreNotMineOsServers()
    {
        var args = new[] { "SCREEN", "-dmS", "my-own-session", "bash" };
        Assert.Null(ProcessManager.TryGetScreenSessionName(args));
    }

    [Fact]
    public void IgnoresLowercaseDashS()
    {
        // -s is screen's shell flag, not the session name.
        var args = new[] { "screen", "-s", "mc-hub", "bash" };
        Assert.Null(ProcessManager.TryGetScreenSessionName(args));
    }

    [Fact]
    public void IgnoresEverythingElse()
    {
        Assert.Null(ProcessManager.TryGetScreenSessionName(new[] { "java", "-jar", "paper.jar" }));
        Assert.Null(ProcessManager.TryGetScreenSessionName(new[] { "screen" }));
        Assert.Null(ProcessManager.TryGetScreenSessionName(Array.Empty<string>()));
    }

    [Fact]
    public void IgnoresAFlagWithNoArgumentAfterIt()
    {
        var args = new[] { "screen", "-list", "-dmS" };
        Assert.Null(ProcessManager.TryGetScreenSessionName(args));
    }
}
