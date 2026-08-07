using System.Text.Json;
using MineOS.Application.Dtos;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers the config.yml mapping behind the BungeeCord proxy editor. The two things
/// worth guarding are that we read BungeeCord's real file shape (listener settings
/// live inside listeners[0], not at the root) and that saving preserves the keys the
/// editor does not model.
/// </summary>
public class BungeeConfigTests
{
    // Trimmed from a config.yml BungeeCord generates on first launch. Keeps the
    // nesting and a few keys the editor never surfaces (groups, permissions).
    private const string SampleConfig = """
        online_mode: true
        ip_forward: false
        player_limit: -1
        timeout: 30000
        network_compression_threshold: 256
        forge_support: false
        log_commands: false
        log_pings: true
        connection_throttle: 4000
        connection_throttle_limit: 3
        stats: 6d9e9d3b-0000-4000-8000-a1b2c3d4e5f6
        groups:
          md_5:
          - admin
        permissions:
          default:
          - bungeecord.command.server
        disabled_commands:
        - disabledcommandhere
        listeners:
        - host: 0.0.0.0:25577
          motd: '&1Another Bungee server'
          max_players: 1
          query_enabled: false
          query_port: 25577
          ping_passthrough: false
          force_default_server: false
          tab_list: GLOBAL_PING
          proxy_protocol: false
          priorities:
          - lobby
          forced_hosts:
            pvp.md-5.net: pvp
        servers:
          lobby:
            address: localhost:25565
            motd: '&1Just another BungeeCord - Forced Host'
            restricted: false
        """;

    [Fact]
    public void Parse_Reads_Root_Level_Settings()
    {
        var config = ParseSample();

        Assert.True(config.Exists);
        Assert.True(config.OnlineMode);
        Assert.False(config.IpForward);
        Assert.Equal(-1, config.PlayerLimit);
        Assert.Equal(30000, config.Timeout);
        Assert.Equal(256, config.NetworkCompressionThreshold);
        Assert.Equal(4000, config.ConnectionThrottle);
        Assert.Equal(3, config.ConnectionThrottleLimit);
    }

    [Fact]
    public void Parse_Reads_Settings_From_First_Listener_Not_Root()
    {
        var config = ParseSample();

        Assert.Equal("0.0.0.0:25577", config.Host);
        Assert.Equal("&1Another Bungee server", config.Motd);
        Assert.Equal(1, config.MaxPlayers);
        Assert.Equal(25577, config.QueryPort);
        Assert.Equal("GLOBAL_PING", config.TabList);
        Assert.Equal(new[] { "lobby" }, config.Priorities);
        Assert.Equal("pvp", Assert.Contains("pvp.md-5.net", config.ForcedHosts));
    }

    [Fact]
    public void Parse_Reads_Backend_Servers()
    {
        var config = ParseSample();

        var lobby = Assert.Contains("lobby", config.Servers);
        Assert.Equal("localhost:25565", lobby.Address);
        Assert.Equal("&1Just another BungeeCord - Forced Host", lobby.Motd);
        Assert.False(lobby.Restricted);
    }

    [Fact]
    public void Parse_Falls_Back_To_Defaults_For_Missing_Keys()
    {
        // A file with nothing but a servers map — every other key should default.
        var config = ServerService.ParseBungeeConfig("servers:\n  lobby:\n    address: localhost:25565\n");
        var defaults = ServerService.BungeeConfigDefaults(exists: true);

        Assert.Equal(defaults.Timeout, config.Timeout);
        Assert.Equal(defaults.Host, config.Host);
        Assert.Equal(defaults.TabList, config.TabList);
        Assert.Equal(defaults.ConnectionThrottle, config.ConnectionThrottle);
        Assert.Empty(config.Priorities);
    }

    [Fact]
    public void Parse_Reports_Empty_Document_As_Defaults()
    {
        var config = ServerService.ParseBungeeConfig("");
        var defaults = ServerService.BungeeConfigDefaults(exists: true);

        // Compared field-by-field: BungeeConfigDto's record equality compares its
        // List/Dictionary members by reference, so two structurally-equal instances
        // are never Equal.
        Assert.True(config.Exists);
        Assert.Equal(defaults.OnlineMode, config.OnlineMode);
        Assert.Equal(defaults.Timeout, config.Timeout);
        Assert.Equal(defaults.Host, config.Host);
        Assert.Equal(defaults.Motd, config.Motd);
        Assert.Equal(defaults.MaxPlayers, config.MaxPlayers);
        Assert.Equal(defaults.TabList, config.TabList);
        Assert.Empty(config.Priorities);
        Assert.Empty(config.ForcedHosts);
        Assert.Empty(config.Servers);
    }

    [Fact]
    public void RoundTrip_Preserves_Edited_Values()
    {
        var edited = ParseSample() with
        {
            Motd = "&aEdited MOTD",
            MaxPlayers = 200,
            IpForward = true,
            Priorities = new List<string> { "lobby", "survival" },
            Servers = new Dictionary<string, BungeeBackendDto>
            {
                ["lobby"] = new("localhost:25565", "&1Lobby", false),
                ["survival"] = new("localhost:25566", "&2Survival", true)
            }
        };

        var reparsed = ServerService.ParseBungeeConfig(
            ServerService.SerializeBungeeConfig(edited, SampleConfig));

        Assert.Equal("&aEdited MOTD", reparsed.Motd);
        Assert.Equal(200, reparsed.MaxPlayers);
        Assert.True(reparsed.IpForward);
        Assert.Equal(new[] { "lobby", "survival" }, reparsed.Priorities);
        Assert.Equal(2, reparsed.Servers.Count);
        Assert.True(Assert.Contains("survival", reparsed.Servers).Restricted);
    }

    [Fact]
    public void RoundTrip_Preserves_Keys_The_Editor_Does_Not_Model()
    {
        // groups / permissions / disabled_commands / stats have no DTO field. Dropping
        // them on save would silently wipe a user's proxy permissions.
        var yaml = ServerService.SerializeBungeeConfig(ParseSample(), SampleConfig);

        Assert.Contains("groups:", yaml);
        Assert.Contains("permissions:", yaml);
        Assert.Contains("disabled_commands:", yaml);
        Assert.Contains("6d9e9d3b-0000-4000-8000-a1b2c3d4e5f6", yaml);
        Assert.Contains("bungeecord.command.server", yaml);
    }

    [Fact]
    public void RoundTrip_Preserves_Additional_Listeners()
    {
        // The editor only exposes listeners[0]; a second listener must survive.
        var twoListeners = SampleConfig.Replace(
            "servers:",
            "- host: 0.0.0.0:25578\n  motd: '&2Second listener'\n  max_players: 5\nservers:");

        var config = ServerService.ParseBungeeConfig(twoListeners);
        var yaml = ServerService.SerializeBungeeConfig(config with { Motd = "&aFirst only" }, twoListeners);
        var reparsed = ServerService.ParseBungeeConfig(yaml);

        Assert.Equal("&aFirst only", reparsed.Motd);
        Assert.Contains("&2Second listener", yaml);
        Assert.Contains("0.0.0.0:25578", yaml);
    }

    [Fact]
    public void Serialize_Without_Existing_File_Emits_A_Usable_Config()
    {
        var config = ServerService.BungeeConfigDefaults(exists: true) with
        {
            Host = "0.0.0.0:25580",
            Servers = new Dictionary<string, BungeeBackendDto>
            {
                ["lobby"] = new("127.0.0.1:25565", "&1Lobby", false)
            },
            Priorities = new List<string> { "lobby" }
        };

        var reparsed = ServerService.ParseBungeeConfig(
            ServerService.SerializeBungeeConfig(config, existingYaml: null));

        Assert.Equal("0.0.0.0:25580", reparsed.Host);
        Assert.Equal(new[] { "lobby" }, reparsed.Priorities);
        Assert.Equal("127.0.0.1:25565", Assert.Contains("lobby", reparsed.Servers).Address);
    }

    [Fact]
    public void Serialize_Tolerates_Null_Collections_From_A_Partial_Request_Body()
    {
        // BungeeConfigDto is bound straight from the PUT body. Its collection
        // properties are declared non-nullable, but System.Text.Json leaves them
        // null when the client omits them — nullable reference types are not
        // enforced at runtime. Without a guard this threw NullReferenceException
        // and surfaced as an unhandled 500.
        var partialBody = """
            {"exists":true,"onlineMode":true,"host":"0.0.0.0:25577",
             "motd":"x","maxPlayers":1,"tabList":"GLOBAL_PING"}
            """;
        var config = JsonSerializer.Deserialize<BungeeConfigDto>(
            partialBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Null(config.Priorities); // guards the premise of this test
        Assert.Null(config.Servers);

        var reparsed = ServerService.ParseBungeeConfig(
            ServerService.SerializeBungeeConfig(config, existingYaml: null));

        Assert.Equal("0.0.0.0:25577", reparsed.Host);
        Assert.Empty(reparsed.Priorities);
        Assert.Empty(reparsed.Servers);
        Assert.Empty(reparsed.ForcedHosts);
    }

    [Fact]
    public void Serialize_Tolerates_A_Null_Backend_Entry()
    {
        var config = ServerService.BungeeConfigDefaults(exists: true) with
        {
            Servers = new Dictionary<string, BungeeBackendDto> { ["lobby"] = null! }
        };

        var reparsed = ServerService.ParseBungeeConfig(
            ServerService.SerializeBungeeConfig(config, existingYaml: null));

        var lobby = Assert.Contains("lobby", reparsed.Servers);
        Assert.Equal("", lobby.Address);
        Assert.False(lobby.Restricted);
    }

    [Fact]
    public void Defaults_Disable_Ping_Logging()
    {
        // MineOS SLP-pings proxies on every heartbeat; BungeeCord's own default of
        // log_pings: true would flood the console with one line per poll.
        Assert.False(ServerService.BungeeConfigDefaults(exists: true).LogPings);
    }

    private static BungeeConfigDto ParseSample() => ServerService.ParseBungeeConfig(SampleConfig);
}
