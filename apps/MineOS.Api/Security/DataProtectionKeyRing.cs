using Microsoft.Data.Sqlite;

namespace MineOS.Api.Security;

/// <summary>
/// Where the Data Protection key ring lives.
///
/// Those keys encrypt <c>LinkedAccount.AccessToken</c>. ASP.NET Core's default store is
/// <c>$HOME/.aspnet/DataProtection-Keys</c>, which for this container is <c>/root/...</c> —
/// part of the image, not the mounted volume. So every image update started with an empty
/// key ring and the token written by the previous one could no longer be decrypted: the
/// account silently unlinked itself, with nothing in the UI to explain why.
///
/// The keys therefore live beside the SQLite database. Whatever a deployment already does
/// to persist the database persists the keys, with no second volume to mount or document.
/// </summary>
public static class DataProtectionKeyRing
{
    private const string DirectoryName = "dataprotection-keys";

    /// <summary>
    /// The key ring directory for a SQLite connection string, or one under
    /// <paramref name="fallbackRoot"/> when the connection string names no file on disk —
    /// in-memory databases, tests, or no configured connection at all.
    /// </summary>
    public static string ResolveDirectory(string? connectionString, string fallbackRoot)
    {
        var databaseDirectory = TryGetDatabaseDirectory(connectionString);

        return databaseDirectory is null
            ? Path.Combine(Path.GetFullPath(fallbackRoot), "data", DirectoryName)
            : Path.Combine(databaseDirectory, DirectoryName);
    }

    /// <summary>
    /// Creates the key ring directory, owner-only where the platform has a say.
    /// </summary>
    /// <remarks>
    /// The permissions are not decoration: the API process runs as root while uid 1000
    /// runs the Minecraft servers, and both can reach the data directory. A default-mode
    /// directory would leave the key files readable by anything a server process can run.
    /// Tightening an existing directory is best effort — a deployment that has moved the
    /// keys under different ownership is not a reason to refuse to start.
    /// </remarks>
    public static DirectoryInfo EnsureDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return Directory.CreateDirectory(path);
        }

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        var directory = Directory.CreateDirectory(path, ownerOnly);

        try
        {
            File.SetUnixFileMode(path, ownerOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left as found.
        }

        return directory;
    }

    private static string? TryGetDatabaseDirectory(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

            if (string.IsNullOrWhiteSpace(dataSource))
            {
                return null;
            }

            // ":memory:" and "file::memory:?cache=shared" name no file, so there is no
            // directory to sit next to — and nothing worth persisting keys alongside.
            if (dataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { Length: > 0 } directory
                ? directory
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a SQLite connection string, or not a usable path. Fall back.
            return null;
        }
    }
}
