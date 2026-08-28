using System.Text.Json;
using MineOS.Application.Dtos;
using MineOS.Domain.ValueObjects;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers the forwarding-security decision and the two file edits behind it.
///
/// The decision is deliberately a pure function over facts gathered elsewhere, so
/// every interesting combination is testable without a filesystem, a proxy, or a
/// running server.
/// </summary>
public class ProxyForwardingTests
{
    private static ForwardingFacts Facts(
        bool isBackend = true,
        ProxyForwardingKind kind = ProxyForwardingKind.VelocityModern,
        LoaderTier tier = LoaderTier.Native,
        bool configured = true,
        bool secretMatches = true,
        bool onlineMode = false) =>
        new(isBackend, kind, tier, configured, secretMatches, onlineMode);

    [Fact]
    public void A_Standalone_Server_Is_Not_Assessed()
    {
        var result = ProxyForwardingRules.Resolve(Facts(isBackend: false));

        Assert.Equal(ProxyForwardingStatus.NotABackend, result.Status);
        Assert.False(result.IsSpoofable);
    }

    [Fact]
    public void Modern_Forwarding_With_Matching_Secrets_Is_Secured()
    {
        var result = ProxyForwardingRules.Resolve(Facts());

        Assert.Equal(ProxyForwardingStatus.Secured, result.Status);
        Assert.False(result.IsSpoofable);
    }

    [Fact]
    public void Online_Mode_Off_Without_Verified_Forwarding_Is_Spoofable()
    {
        // The combination this whole feature exists to catch: the backend has
        // stopped authenticating players and nothing has taken the job over.
        var result = ProxyForwardingRules.Resolve(
            Facts(kind: ProxyForwardingKind.VelocityUnverified, configured: false, secretMatches: false));

        Assert.Equal(ProxyForwardingStatus.Misconfigured, result.Status);
        Assert.True(result.IsSpoofable);
    }

    [Fact]
    public void A_Secret_Mismatch_Is_Spoofable_Even_Though_Both_Sides_Are_Configured()
    {
        // Looks configured from either side alone. Players cannot actually be
        // verified, so online-mode=false still leaves the server open.
        var result = ProxyForwardingRules.Resolve(Facts(secretMatches: false));

        Assert.Equal(ProxyForwardingStatus.Misconfigured, result.Status);
        Assert.True(result.IsSpoofable);
    }

    [Fact]
    public void Online_Mode_Left_On_Is_Broken_But_Not_Dangerous()
    {
        // Players cannot join through the proxy, but the server still checks
        // identities itself, so nobody can impersonate anyone.
        var result = ProxyForwardingRules.Resolve(
            Facts(kind: ProxyForwardingKind.VelocityUnverified, configured: false, onlineMode: true));

        Assert.Equal(ProxyForwardingStatus.Securable, result.Status);
        Assert.False(result.IsSpoofable);
    }

    [Fact]
    public void Verified_Forwarding_With_Online_Mode_On_Is_Misconfigured_But_Safe()
    {
        var result = ProxyForwardingRules.Resolve(Facts(onlineMode: true));

        Assert.Equal(ProxyForwardingStatus.Misconfigured, result.Status);
        Assert.False(result.IsSpoofable);
    }

    [Fact]
    public void A_Bungeecord_Backend_Is_Unverifiable_However_It_Is_Configured()
    {
        // ip_forward carries no signature, so there is nothing for the backend to
        // check and no fix to offer beyond keeping the port unreachable.
        var result = ProxyForwardingRules.Resolve(
            Facts(kind: ProxyForwardingKind.BungeeCord, configured: true, secretMatches: true));

        Assert.Equal(ProxyForwardingStatus.Unverifiable, result.Status);
        Assert.True(result.IsSpoofable);
    }

    [Fact]
    public void A_Forge_Backend_Is_Unverifiable_And_Reported_Spoofable_When_Open()
    {
        var result = ProxyForwardingRules.Resolve(Facts(tier: LoaderTier.Unsupported));

        Assert.Equal(ProxyForwardingStatus.Unverifiable, result.Status);
        Assert.True(result.IsSpoofable);
    }

    [Fact]
    public void A_Fabric_Backend_Without_The_Mod_Is_Securable()
    {
        var result = ProxyForwardingRules.Resolve(
            Facts(tier: LoaderTier.ModRequired, configured: false, secretMatches: false, onlineMode: true));

        Assert.Equal(ProxyForwardingStatus.Securable, result.Status);
        Assert.False(result.IsSpoofable);
    }

    [Theory]
    [InlineData("paper", LoaderTier.Native)]
    [InlineData("purpur", LoaderTier.Native)]
    [InlineData("folia", LoaderTier.Native)]
    [InlineData("fabric", LoaderTier.ModRequired)]
    [InlineData("quilt", LoaderTier.ModRequired)]
    [InlineData("forge", LoaderTier.Unsupported)]
    [InlineData("neoforge", LoaderTier.Unsupported)]
    [InlineData("vanilla", LoaderTier.Unsupported)]
    // Modern forwarding is a Paper feature, not a Bukkit one.
    [InlineData("spigot", LoaderTier.Unsupported)]
    [InlineData("craftbukkit", LoaderTier.Unsupported)]
    [InlineData(null, LoaderTier.Unsupported)]
    public void Loader_Tiers_Reflect_What_Each_Server_Can_Verify(string? loader, LoaderTier expected)
    {
        Assert.Equal(expected, ProxyForwardingRules.TierFor(loader));
    }

    [Fact]
    public void Two_Missing_Secrets_Are_Two_Holes_Not_A_Match()
    {
        Assert.False(ProxyForwardingService.SecretsAgree(null, null));
        Assert.False(ProxyForwardingService.SecretsAgree("", ""));
        Assert.False(ProxyForwardingService.SecretsAgree("   ", "   "));
        Assert.False(ProxyForwardingService.SecretsAgree("abc", null));
    }

    [Fact]
    public void Secrets_Match_Ignoring_Surrounding_Whitespace()
    {
        // Velocity writes the file without a trailing newline; editors add one.
        Assert.True(ProxyForwardingService.SecretsAgree("s3cret", "s3cret\n"));
        Assert.False(ProxyForwardingService.SecretsAgree("s3cret", "s3cretx"));
    }

    [Theory]
    [InlineData("127.0.0.1:25566", 25566, true)]
    [InlineData("localhost:25566", 25566, true)]
    [InlineData("mineos-api:25566", 25566, true)]
    [InlineData("[::1]:25566", 25566, true)]
    [InlineData("127.0.0.1:25565", 25566, false)]
    [InlineData("127.0.0.1", 25566, false)]
    [InlineData("", 25566, false)]
    public void Backend_Addresses_Are_Matched_On_Port(string address, int port, bool expected)
    {
        Assert.Equal(expected, ProxyForwardingService.AddressTargets(address, port));
    }

    private const string PaperGlobalSample = """
        _version: 29
        block-updates:
          disable-chorus-plant-updates: false
        chunk-loading-advanced:
          auto-config-send-distance: true
        proxies:
          bungee-cord:
            online-mode: true
          velocity:
            enabled: false
            online-mode: true
            secret: ''
        """;

    [Fact]
    public void Paper_Velocity_Block_Is_Enabled_Without_Touching_The_Rest_Of_The_File()
    {
        var result = ProxyForwardingService.UpsertPaperVelocityBlock(PaperGlobalSample, "topsecret");

        // The keys we came for.
        Assert.Contains("secret: topsecret", result);

        // Everything Paper owns and we do not model must survive: regenerating
        // this file instead of editing it would silently reset the server.
        Assert.Contains("_version: 29", result);
        Assert.Contains("disable-chorus-plant-updates", result);
        Assert.Contains("auto-config-send-distance", result);
        Assert.Contains("bungee-cord", result);
    }

    [Fact]
    public void Paper_Velocity_Block_Is_Created_When_The_File_Does_Not_Exist_Yet()
    {
        // Paper fills in every key it owns on next start, so a minimal file is safe.
        var result = ProxyForwardingService.UpsertPaperVelocityBlock(null, "topsecret");

        Assert.Contains("proxies:", result);
        Assert.Contains("velocity:", result);
        Assert.Contains("enabled: true", result);
        Assert.Contains("secret: topsecret", result);
    }

    [Fact]
    public void The_Dto_Puts_Enum_Names_On_The_Wire_Not_Numbers()
    {
        // Regression test for a bug the C# and TypeScript suites both missed by
        // testing either side in isolation: System.Text.Json serializes enums as
        // integers by default, so the API sent {"status":3} while the web client
        // matched on 'Securable'. Nothing threw — the panel just rendered with an
        // empty headline, which is the worst way for a security warning to fail.
        var dto = new BackendForwardingDto(
            ServerName: "freemancraft",
            Status: ProxyForwardingStatus.Securable.ToString(),
            IsSpoofable: false,
            ProxyKind: ProxyForwardingKind.VelocityModern.ToString(),
            Tier: LoaderTier.Native.ToString(),
            ProxyName: "hub",
            Loader: "paper",
            ServerOnlineMode: true,
            BackendForwardingConfigured: false,
            SecretMatches: false,
            Exposure: ExposureVerdict.Unknown.ToString(),
            ExposureDetail: null,
            RemediationAction: "secure");

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("\"status\":\"Securable\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"proxyKind\":\"VelocityModern\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"tier\":\"Native\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"exposure\":\"Unknown\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_Status_And_Verdict_Name_Matches_What_The_Web_Client_Expects()
    {
        // These strings are duplicated in apps/web/src/lib/api/types.ts. If an enum
        // member is ever renamed, this fails here rather than silently blanking a
        // warning in the browser.
        Assert.Equal(
            new[] { "NotABackend", "Secured", "Misconfigured", "Securable", "Unverifiable" },
            Enum.GetNames<ProxyForwardingStatus>());
        Assert.Equal(
            new[] { "None", "VelocityModern", "VelocityUnverified", "BungeeCord" },
            Enum.GetNames<ProxyForwardingKind>());
        Assert.Equal(
            new[] { "Native", "ModRequired", "Unsupported" },
            Enum.GetNames<LoaderTier>());
        Assert.Equal(
            new[] { "Unknown", "NotExposed", "Exposed" },
            Enum.GetNames<ExposureVerdict>());
    }

    private static JsonElement Container(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Host_Networking_Exposes_Every_Port()
    {
        var (verdict, detail) = DockerPortInspector.Evaluate(
            Container("""{"HostConfig":{"NetworkMode":"host","PortBindings":{}}}"""), 25566);

        Assert.Equal(ExposureVerdict.Exposed, verdict);
        Assert.Contains("host networking", detail);
    }

    [Fact]
    public void A_Published_Port_Is_Exposed()
    {
        var (verdict, _) = DockerPortInspector.Evaluate(
            Container("""
                {"HostConfig":{"NetworkMode":"bridge","PortBindings":{
                  "25566/tcp":[{"HostIp":"","HostPort":"25566"}]}}}
                """), 25566);

        Assert.Equal(ExposureVerdict.Exposed, verdict);
    }

    [Fact]
    public void An_Unpublished_Port_Is_Not_Exposed()
    {
        var (verdict, _) = DockerPortInspector.Evaluate(
            Container("""
                {"HostConfig":{"NetworkMode":"bridge","PortBindings":{
                  "25565/tcp":[{"HostIp":"","HostPort":"25565"}]}}}
                """), 25566);

        Assert.Equal(ExposureVerdict.NotExposed, verdict);
    }

    [Fact]
    public void A_Key_With_No_Bindings_Does_Not_Count_As_Published()
    {
        var (verdict, _) = DockerPortInspector.Evaluate(
            Container("""{"HostConfig":{"NetworkMode":"bridge","PortBindings":{"25566/tcp":null}}}"""), 25566);

        Assert.Equal(ExposureVerdict.NotExposed, verdict);
    }

    [Fact]
    public void An_Unreadable_Answer_Is_Unknown_Never_Safe()
    {
        // The one behaviour that must not regress: no evidence is not evidence of
        // safety, because this verdict is the only control for backends that
        // cannot verify forwarded players at all.
        Assert.Equal(ExposureVerdict.Unknown, DockerPortInspector.Evaluate(Container("{}"), 25566).Verdict);
        Assert.Equal(
            ExposureVerdict.Unknown,
            DockerPortInspector.Evaluate(Container("""{"HostConfig":{"NetworkMode":"bridge"}}"""), 25566).Verdict);
    }
}
