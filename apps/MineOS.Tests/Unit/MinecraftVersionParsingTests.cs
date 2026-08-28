using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers ServerService.ParseMinecraftVersion, which answers "which Minecraft
/// version does this server run?" from the configured jar value.
///
/// This exists because the answer was previously taken from the loader version,
/// which for Fabric and Quilt is an entirely different number. Every Modrinth
/// query for a Fabric server filtered on a game version that does not exist, so
/// mod search returned nothing at all.
/// </summary>
public class MinecraftVersionParsingTests
{
    [Theory]
    // Fabric and Quilt tag the game version; the trailing number is the loader.
    [InlineData("fabric-server-mc.1.21.1-loader.0.19.3.jar", "1.21.1")]
    [InlineData("fabric-server-mc.1.20-loader.0.15.0.jar", "1.20")]
    [InlineData("quilt-server-mc.1.21.4-loader.0.26.0.jar", "1.21.4")]
    // Server jars that carry the version literally; later numbers are build ids.
    [InlineData("paper-1.21.11-132.jar", "1.21.11")]
    [InlineData("purpur-1.20.4-2147.jar", "1.20.4")]
    [InlineData("spigot-1.20.1.jar", "1.20.1")]
    [InlineData("minecraft_server.1.20.1.jar", "1.20.1")]
    // Classic Forge puts the game version first, its own second.
    [InlineData("forge-1.20.1-47.2.0.jar", "1.20.1")]
    // Nothing usable: better to return null and let the caller fall back to the
    // configured profile than to filter on a wrong value.
    [InlineData("server.jar", null)]
    [InlineData("custom-build.jar", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_Game_Version_Is_Read_From_The_Jar_Value(string? jar, string? expected)
    {
        Assert.Equal(expected, ServerService.ParseMinecraftVersion(jar));
    }

    [Theory]
    // NeoForge names carry no Minecraft version: its own encodes it, so 21.1.227
    // is Minecraft 1.21.1 and a zero minor means the .0 release (21.0.x -> 1.21).
    [InlineData("neoforge-21.1.227.jar", "1.21.1")]
    [InlineData("neoforge-20.4.190.jar", "1.20.4")]
    [InlineData("neoforge-21.0.100.jar", "1.21")]
    [InlineData("@libraries/net/neoforged/neoforge/21.1.227/unix_args.txt", "1.21.1")]
    public void NeoForge_Versions_Are_Translated_To_Their_Minecraft_Version(string jar, string expected)
    {
        Assert.Equal(expected, ServerService.ParseMinecraftVersion(jar));
    }

    [Fact]
    public void The_Loader_Version_Is_Never_Returned_As_The_Game_Version()
    {
        // The specific regression: 0.19.3 is the Fabric loader, not a Minecraft
        // version, and Modrinth has no mods for it.
        var parsed = ServerService.ParseMinecraftVersion("fabric-server-mc.1.21.1-loader.0.19.3.jar");

        Assert.Equal("1.21.1", parsed);
        Assert.NotEqual("0.19.3", parsed);
    }
}
