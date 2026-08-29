// apps/MineOS.Tests/Unit/CrashLogRedactorTests.cs
using MineOS.Application.Dtos;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class CrashLogRedactorTests
{
    private static readonly RedactionOptions Default =
        new(RedactPaths: true, RedactPlayerNames: true, KnownPlayers: new[] { "Steve", "Alex" });

    private static string Redact(string input, RedactionOptions? options = null) =>
        CrashLogRedactor.Redact(input, options ?? Default).Text;

    [Fact]
    public void PreservesModFilenameWhenRedactingPaths()
    {
        // The mod filename is usually the answer. Over-redaction makes the feature useless.
        var result = Redact("at /home/dfreeman/mineos/servers/smp/mods/jei-1.20.1-15.2.0.27.jar");

        Assert.Contains("jei-1.20.1-15.2.0.27.jar", result);
        Assert.DoesNotContain("dfreeman", result);
    }

    [Fact]
    public void RedactsPlayerIpAndPort()
    {
        var result = Redact("Steve[/192.168.1.50:52344] logged in with entity id 214");

        Assert.DoesNotContain("192.168.1.50", result);
        Assert.DoesNotContain("52344", result);
    }

    [Fact]
    public void RedactsTokenShapedStrings()
    {
        var result = Redact("DiscordSRV token: EXAMPLEFAKETOKENFORTESTS.abcdef.notarealsecretjustatestvalue");

        Assert.DoesNotContain("EXAMPLEFAKETOKENFORTESTS", result);
        Assert.Contains("<redacted-secret>", result);
    }

    [Fact]
    public void RedactsCredentialLines()
    {
        var result = Redact("rcon.password=hunter2");

        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("rcon.password=", result);
    }

    [Fact]
    public void GivesTheSamePlayerTheSamePseudonymThroughout()
    {
        var result = Redact("Steve joined. Alex joined. Steve left.");

        var first = result.IndexOf("<player", StringComparison.Ordinal);
        var pseudonym = result.Substring(first, result.IndexOf('>', first) - first + 1);
        Assert.Equal(2, CountOccurrences(result, pseudonym));
        Assert.DoesNotContain("Steve", result);
    }

    [Fact]
    public void MandatoryRulesStillApplyWhenOptionalRulesAreDisabled()
    {
        var loose = new RedactionOptions(RedactPaths: false, RedactPlayerNames: false, KnownPlayers: Array.Empty<string>());
        var result = Redact("Steve[/192.168.1.50:52344] rcon.password=hunter2", loose);

        Assert.DoesNotContain("192.168.1.50", result);
        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("Steve", result); // opted out, so the name stays
    }

    [Fact]
    public void ReportsWhichRulesFired()
    {
        var result = CrashLogRedactor.Redact("connection from /10.0.0.4:25565", Default);

        Assert.Contains("ip-address", result.RulesApplied);
        Assert.DoesNotContain("email", result.RulesApplied);
    }

    [Fact]
    public void RedactsABracketedIpv6AddressWithAPort()
    {
        // How vanilla writes a player on a residential IPv6 connection.
        var result = CrashLogRedactor.Redact(
            "Steve[/[2001:db8:85a3::8a2e:370:7334]:52344] logged in with entity id 214", Default);

        Assert.DoesNotContain("2001:db8", result.Text);
        Assert.DoesNotContain("8a2e", result.Text);
        Assert.DoesNotContain("52344", result.Text);
        Assert.Contains("<ip>", result.Text);
        Assert.Contains("ip-address", result.RulesApplied);
    }

    [Fact]
    public void RedactsABareIpv6Address()
    {
        var result = Redact("[12:04:31] [Server thread/INFO]: connection from 2001:db8::1 refused");

        Assert.DoesNotContain("2001:db8", result);
        Assert.Contains("<ip>", result);
    }

    [Fact]
    public void RedactsLoopbackIpv6()
    {
        var result = Redact("Starting Minecraft server on ::1");

        Assert.DoesNotContain("::1", result);
        Assert.Contains("<ip>", result);
    }

    [Fact]
    public void LeavesOrdinaryLogTextAlone()
    {
        // The IPv6 rule must not eat timestamps, stack frames or version numbers: every one of
        // them is hex groups separated by colons or dots to a loose pattern.
        const string line =
            "[12:04:31] [Server thread/INFO]: Done (21.402s)! For help, type \"help\"";
        var result = CrashLogRedactor.Redact(line, Default);

        Assert.Equal(line, result.Text);
        Assert.DoesNotContain("ip-address", result.RulesApplied);
    }

    [Fact]
    public void LeavesStackFrameLineNumbersAlone()
    {
        const string line = "\tat net.minecraft.server.MinecraftServer.tick(MinecraftServer.java:123)";

        Assert.Equal(line, CrashLogRedactor.Redact(line, Default).Text);
    }

    [Fact]
    public void ReportsTheIpRuleOnlyOnceWhenBothFamiliesFire()
    {
        var result = CrashLogRedactor.Redact(
            "from /10.0.0.4:25565 and from [2001:db8::1]:25565", Default);

        Assert.Equal(1, result.RulesApplied.Count(r => r == "ip-address"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
