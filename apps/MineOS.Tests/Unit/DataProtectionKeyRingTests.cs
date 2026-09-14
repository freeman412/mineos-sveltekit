using MineOS.Api.Security;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers DataProtectionKeyRing, which decides where the Data Protection key ring is
/// written.
///
/// It exists because the default store — $HOME/.aspnet/DataProtection-Keys — is inside
/// the container image rather than the mounted data volume. Every image update therefore
/// produced a fresh key ring, and LinkedAccount.AccessToken, encrypted under the previous
/// one, became unreadable: the account unlinked itself and the user was asked to link
/// again with no explanation. Putting the keys next to the database ties their lifetime
/// to the thing every deployment already persists.
/// </summary>
public class DataProtectionKeyRingTests
{
    [Fact]
    public void PutsKeysBesideTheDatabase()
    {
        var resolved = DataProtectionKeyRing.ResolveDirectory(
            "Data Source=/app/data/mineos.db", fallbackRoot: "/app");

        Assert.Equal(Path.Combine("/app/data", "dataprotection-keys"), resolved);
    }

    [Fact]
    public void ResolvesARelativeDataSourceToAnAbsolutePath()
    {
        // appsettings.json ships "Data Source=./data/mineos.db". A relative key ring path
        // would follow the working directory, which is not the app's to rely on.
        var resolved = DataProtectionKeyRing.ResolveDirectory(
            "Data Source=./data/mineos.db", fallbackRoot: "/app");

        Assert.True(Path.IsPathRooted(resolved));
        Assert.EndsWith(Path.Combine("data", "dataprotection-keys"), resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=file::memory:?cache=shared")]
    [InlineData("Server=db;Database=mineos")]
    public void FallsBackUnderTheContentRootWhenNoDatabaseFileIsNamed(string? connectionString)
    {
        // An in-memory database has no directory to sit beside, and a connection string
        // for another provider names nothing this code understands. Neither is a reason
        // to hand Data Protection a path it cannot use.
        var resolved = DataProtectionKeyRing.ResolveDirectory(connectionString, fallbackRoot: "/app");

        Assert.Equal(Path.Combine(Path.GetFullPath("/app"), "data", "dataprotection-keys"), resolved);
    }

    [Fact]
    public void CreatesTheDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mineos-keyring-{Guid.NewGuid():N}");

        try
        {
            var created = DataProtectionKeyRing.EnsureDirectory(Path.Combine(root, "dataprotection-keys"));

            Assert.True(created.Exists);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CreatesTheDirectoryOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permissions are not a thing to assert here.
        }

        var root = Path.Combine(Path.GetTempPath(), $"mineos-keyring-{Guid.NewGuid():N}");

        try
        {
            // The API runs as root while uid 1000 runs the Minecraft servers, and both can
            // reach the data directory. Group or world access would expose signing keys to
            // anything a server process can run.
            var path = Path.Combine(root, "dataprotection-keys");
            DataProtectionKeyRing.EnsureDirectory(path);

            var mode = File.GetUnixFileMode(path);

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                mode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TightensADirectoryThatAlreadyExists()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"mineos-keyring-{Guid.NewGuid():N}");

        try
        {
            // An install that already wrote keys under a laxer mode should be corrected on
            // the next start, not left as it was found.
            var path = Path.Combine(root, "dataprotection-keys");
            Directory.CreateDirectory(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            DataProtectionKeyRing.EnsureDirectory(path);

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
