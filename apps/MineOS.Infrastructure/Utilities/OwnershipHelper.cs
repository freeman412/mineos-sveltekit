using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MineOS.Infrastructure.Utilities;

public static class OwnershipHelper
{
    private const string PrimaryChownPath = "/bin/chown";
    private const string FallbackChownPath = "/usr/bin/chown";

    public static async Task ChangeOwnershipAsync(
        string path,
        int uid,
        int gid,
        ILogger logger,
        CancellationToken cancellationToken,
        bool recursive = false)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var chownPath = ResolveChownPath();
        if (chownPath == null)
        {
            logger.LogWarning("chown not available; skipping ownership set for {Path}", path);
            return;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = chownPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (recursive)
        {
            process.StartInfo.ArgumentList.Add("-R");
        }

        process.StartInfo.ArgumentList.Add($"{uid}:{gid}");
        process.StartInfo.ArgumentList.Add(path);

        process.Start();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            logger.LogWarning("Failed to change ownership of {Path} to {Uid}:{Gid}: {Error}", path, uid, gid, error);
        }
    }

    public static void TrySetOwnership(
        string path,
        int uid,
        int gid,
        ILogger logger,
        bool recursive = false)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var chownPath = ResolveChownPath();
        if (chownPath == null)
        {
            logger.LogWarning("chown not available; skipping ownership set for {Path}", path);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = chownPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (recursive)
            {
                psi.ArgumentList.Add("-R");
            }

            psi.ArgumentList.Add($"{uid}:{gid}");
            psi.ArgumentList.Add(path);

            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set ownership for {Path}", path);
        }
    }

    private static string? ResolveChownPath()
    {
        if (File.Exists(PrimaryChownPath))
        {
            return PrimaryChownPath;
        }

        if (File.Exists(FallbackChownPath))
        {
            return FallbackChownPath;
        }

        return null;
    }
}
