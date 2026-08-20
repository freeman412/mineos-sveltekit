using MineOS.Application.Dtos;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers the two decisions behind installing FabricProxy-Lite: recognising the
/// mod on disk, and choosing which build to fetch. Both are pure, so neither test
/// needs Modrinth or a server.
/// </summary>
public class FabricForwardingModTests
{
    [Theory]
    [InlineData("FabricProxy-Lite-0.9.0.jar", true)]
    [InlineData("fabricproxy-lite.jar", true)]
    [InlineData("fabricproxy_lite-1.2.3.jar", true)]
    [InlineData("FabricProxy Lite.jar", true)]
    [InlineData("fabricproxy.jar", false)]        // the older, different mod
    [InlineData("lithium-fabric-0.11.jar", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_Mod_Jar_Is_Recognised_Across_Its_Spellings(string? fileName, bool expected)
    {
        Assert.Equal(expected, FabricForwardingMod.IsForwardingModJar(fileName));
    }

    [Fact]
    public void A_Renamed_Disabled_Jar_Is_Not_A_Jar()
    {
        // MineOS disables a mod by renaming it away from .jar. A disabled
        // FabricProxy-Lite verifies nothing, so counting it as installed would
        // report a spoofable server as secured.
        Assert.False(FabricForwardingMod.IsForwardingModJar("FabricProxy-Lite-0.9.0.jar.disabled"));
    }

    [Theory]
    [InlineData("fabric-server-mc.1.21.1-loader.0.19.3.jar", "1.21.1")]
    [InlineData("fabric-server-mc.1.20-loader.0.15.0.jar", "1.20")]
    [InlineData("quilt-server-mc.1.21.4-loader.0.26.0.jar", "1.21.4")]
    [InlineData("server.jar", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_Minecraft_Version_Comes_From_The_Jar_Name(string? jar, string? expected)
    {
        // Regression test for a bug found running this against a real Fabric
        // install: DetectLoaderAsync reports the LOADER version for Fabric
        // (0.19.3), so using it as the game version asked Modrinth for builds
        // supporting "Minecraft 0.19.3" and the install refused every time.
        Assert.Equal(expected, FabricForwardingMod.TryParseMinecraftVersion(jar));
    }

    [Fact]
    public void The_Loader_Version_Is_Never_Mistaken_For_The_Game_Version()
    {
        Assert.Equal("1.21.1",
            FabricForwardingMod.TryParseMinecraftVersion("fabric-server-mc.1.21.1-loader.0.19.3.jar"));
    }

    private static ModrinthVersionDto Version(
        string id, string number, string published, string[] gameVersions, string[] loaders,
        ModrinthVersionFileDto[]? files = null, ModrinthDependencyDto[]? dependencies = null) =>
        new(id, "proj", number, number, DateTimeOffset.Parse(published), 0,
            gameVersions, loaders,
            files ?? new[] { new ModrinthVersionFileDto($"https://cdn/{number}.jar", $"{number}.jar", 1000, true) },
            dependencies ?? Array.Empty<ModrinthDependencyDto>());

    [Fact]
    public void The_Newest_Build_For_This_Minecraft_Version_Wins()
    {
        var versions = new[]
        {
            Version("a", "0.8.0", "2026-01-01T00:00:00Z", new[] { "1.21.1" }, new[] { "fabric" }),
            Version("b", "0.9.0", "2026-06-01T00:00:00Z", new[] { "1.21.1" }, new[] { "fabric" }),
            Version("c", "1.0.0", "2026-07-01T00:00:00Z", new[] { "1.21.4" }, new[] { "fabric" }),
        };

        var chosen = FabricForwardingMod.SelectVersion(versions, "1.21.1");

        Assert.Equal("0.9.0", chosen?.VersionNumber);
    }

    [Fact]
    public void No_Matching_Minecraft_Version_Returns_Null_Rather_Than_A_Guess()
    {
        // Installing a build for the wrong Minecraft version leaves a server that
        // looks secured and verifies nothing, so this must refuse, not approximate.
        var versions = new[]
        {
            Version("a", "1.0.0", "2026-07-01T00:00:00Z", new[] { "1.21.4" }, new[] { "fabric" }),
        };

        Assert.Null(FabricForwardingMod.SelectVersion(versions, "1.20.1"));
    }

    [Fact]
    public void Builds_For_Other_Loaders_Are_Ignored()
    {
        var versions = new[]
        {
            Version("a", "9.9.9", "2026-08-01T00:00:00Z", new[] { "1.21.1" }, new[] { "forge" }),
            Version("b", "0.9.0", "2026-06-01T00:00:00Z", new[] { "1.21.1" }, new[] { "fabric" }),
        };

        Assert.Equal("0.9.0", FabricForwardingMod.SelectVersion(versions, "1.21.1")?.VersionNumber);
    }

    [Fact]
    public void An_Unknown_Minecraft_Version_Falls_Back_To_The_Newest_Fabric_Build()
    {
        var versions = new[]
        {
            Version("a", "0.8.0", "2026-01-01T00:00:00Z", new[] { "1.21.1" }, new[] { "fabric" }),
            Version("b", "1.0.0", "2026-07-01T00:00:00Z", new[] { "1.21.4" }, new[] { "fabric" }),
        };

        Assert.Equal("1.0.0", FabricForwardingMod.SelectVersion(versions, null)?.VersionNumber);
    }

    [Fact]
    public void Selecting_From_An_Empty_List_Is_Null_Not_A_Crash()
    {
        Assert.Null(FabricForwardingMod.SelectVersion(Array.Empty<ModrinthVersionDto>(), "1.21.1"));
        Assert.Null(FabricForwardingMod.SelectFile(null));
    }

    [Fact]
    public void The_Primary_File_Is_Chosen_Over_Sources_Jars()
    {
        // Modrinth versions often carry sources/javadoc jars alongside the mod.
        // Installing one of those succeeds and does nothing.
        var version = Version("a", "0.9.0", "2026-06-01T00:00:00Z", new[] { "1.21.1" }, new[] { "fabric" },
            new[]
            {
                new ModrinthVersionFileDto("https://cdn/sources.jar", "fabricproxy-lite-sources.jar", 10, false),
                new ModrinthVersionFileDto("https://cdn/mod.jar", "fabricproxy-lite-0.9.0.jar", 100, true),
            });

        Assert.Equal("fabricproxy-lite-0.9.0.jar", FabricForwardingMod.SelectFile(version)?.FileName);
    }

    [Fact]
    public void Required_Dependencies_Are_Collected_And_Optional_Ones_Ignored()
    {
        // Regression test for a bug found by starting a real Fabric server after
        // the install: FabricProxy-Lite hard-requires Fabric API, and Fabric
        // refuses to BOOT when a required dependency is missing. Installing the
        // mod alone left the server unable to start at all.
        var version = Version("a", "2.10.1", "2026-06-01T00:00:00Z", new[] { "1.21.1" }, new[] { "fabric" },
            dependencies: new[]
            {
                new ModrinthDependencyDto("P7dR8mSH", null, "required"),   // fabric-api
                new ModrinthDependencyDto("optional1", null, "optional"),
                new ModrinthDependencyDto(null, "someVersion", "required"), // no project id to fetch
                new ModrinthDependencyDto("P7dR8mSH", null, "required"),   // duplicate
            });

        var required = FabricForwardingMod.RequiredDependencyProjects(version);

        Assert.Equal(new[] { "P7dR8mSH" }, required);
    }

    [Fact]
    public void A_Version_With_No_Dependencies_Yields_An_Empty_List()
    {
        var version = Version("a", "1.0.0", "2026-06-01T00:00:00Z", new[] { "1.21.1" }, new[] { "fabric" });

        Assert.Empty(FabricForwardingMod.RequiredDependencyProjects(version));
        Assert.Empty(FabricForwardingMod.RequiredDependencyProjects(null));
    }
}
