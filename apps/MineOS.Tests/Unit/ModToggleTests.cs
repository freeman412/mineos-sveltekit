using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class ModToggleTests : IDisposable
{
    private readonly string _tempDir;

    public ModToggleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mineos-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "mods"));
    }

    [Fact]
    public void DisableMod_Renames_Jar_To_Disabled()
    {
        var newName = ModService.ComputeToggleName("testmod.jar", enabled: false);
        Assert.Equal("testmod.jar.disabled", newName);
    }

    [Fact]
    public void EnableMod_Renames_Disabled_To_Jar()
    {
        var newName = ModService.ComputeToggleName("testmod.jar.disabled", enabled: true);
        Assert.Equal("testmod.jar", newName);
    }

    [Fact]
    public void DisableMod_Already_Disabled_Returns_Same()
    {
        var newName = ModService.ComputeToggleName("testmod.jar.disabled", enabled: false);
        Assert.Equal("testmod.jar.disabled", newName);
    }

    [Fact]
    public void EnableMod_Already_Enabled_Returns_Same()
    {
        var newName = ModService.ComputeToggleName("testmod.jar", enabled: true);
        Assert.Equal("testmod.jar", newName);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);
}
