using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class BackupService : IBackupService
{
    private readonly ILogger<BackupService> _logger;
    private readonly HostOptions _hostOptions;

    public BackupService(
        ILogger<BackupService> logger,
        IOptions<HostOptions> hostOptions)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetBackupPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, "backups", serverName);

    public async Task<IEnumerable<IncrementEntryDto>> ListBackupsAsync(string serverName, CancellationToken cancellationToken)
    {
        var backupPath = GetBackupPath(serverName);
        if (!Directory.Exists(backupPath))
        {
            return Enumerable.Empty<IncrementEntryDto>();
        }

        // Use rdiff-backup v2 syntax to list increments
        var psi = new ProcessStartInfo
        {
            FileName = "rdiff-backup",
            Arguments = $"list increments \"{backupPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start rdiff-backup process");
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("rdiff-backup list failed with exit code {ExitCode}: {Error}", process.ExitCode, errorOutput);
            return Enumerable.Empty<IncrementEntryDto>();
        }

        _logger.LogInformation("rdiff-backup list output for {ServerName}: {Output}", serverName, output);
        var increments = ParseIncrementsV2(output);
        _logger.LogInformation("Parsed {Count} increments for {ServerName}", increments.Count(), serverName);
        return increments;
    }

    public async Task CreateBackupAsync(string serverName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        var backupPath = GetBackupPath(serverName);

        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        // Ensure backup directory exists
        Directory.CreateDirectory(backupPath);

        // Use rdiff-backup to create incremental backup
        var psi = new ProcessStartInfo
        {
            FileName = "rdiff-backup",
            Arguments = $"backup \"{serverPath}\" \"{backupPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start rdiff-backup process");
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Backup failed: {error}");
        }

        _logger.LogInformation("Created backup for server {ServerName}", serverName);
    }

    public async Task RestoreBackupAsync(string serverName, string timestamp, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        var backupPath = GetBackupPath(serverName);

        if (!Directory.Exists(backupPath))
        {
            throw new DirectoryNotFoundException($"No backups found for server '{serverName}'");
        }

        // Use rdiff-backup to restore to specific increment
        var psi = new ProcessStartInfo
        {
            FileName = "rdiff-backup",
            Arguments = $"restore --at \"{timestamp}\" \"{backupPath}\" \"{serverPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start rdiff-backup process");
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Restore failed: {error}");
        }

        _logger.LogInformation("Restored backup for server {ServerName} from {Timestamp}", serverName, timestamp);
    }

    public async Task PruneBackupsAsync(string serverName, int keepCount, CancellationToken cancellationToken)
    {
        var backupPath = GetBackupPath(serverName);

        if (!Directory.Exists(backupPath))
        {
            return;
        }

        // Use rdiff-backup to remove old increments, keeping only the specified count
        var psi = new ProcessStartInfo
        {
            FileName = "rdiff-backup",
            Arguments = $"remove increments --older-than {keepCount}B \"{backupPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start rdiff-backup process");
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Prune failed: {error}");
        }

        _logger.LogInformation("Pruned backups for server {ServerName}, keeping {KeepCount}", serverName, keepCount);
    }

    public async Task<IEnumerable<IncrementEntryDto>> GetBackupSizesAsync(string serverName, CancellationToken cancellationToken)
    {
        var backupPath = GetBackupPath(serverName);
        if (!Directory.Exists(backupPath))
        {
            return Enumerable.Empty<IncrementEntryDto>();
        }

        // Use rdiff-backup to list increment sizes
        var psi = new ProcessStartInfo
        {
            FileName = "rdiff-backup",
            Arguments = $"--list-increment-sizes \"{backupPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start rdiff-backup process");
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("rdiff-backup list-increment-sizes failed");
            return Enumerable.Empty<IncrementEntryDto>();
        }

        return ParseIncrementSizes(output);
    }

    private IEnumerable<IncrementEntryDto> ParseIncrementsV2(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var increments = new List<IncrementEntryDto>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip header lines and "found N increments" lines
            if (trimmed.Contains("found") || trimmed.Contains("increment") || string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Try to parse as ISO timestamp first
            if (DateTimeOffset.TryParse(trimmed, out var time))
            {
                _logger.LogDebug("Parsed increment timestamp: {Time}", time);
                increments.Add(new IncrementEntryDto(
                    time,
                    "backup",
                    null,
                    null
                ));
                continue;
            }

            // Try to extract timestamp from line (rdiff-backup might prefix with increment type)
            // Example: "current_mirror.2024-01-10T19:50:59+00:00.data"
            var match = Regex.Match(trimmed, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:[+-]\d{2}:\d{2}|Z))");
            if (match.Success && DateTimeOffset.TryParse(match.Groups[1].Value, out var extractedTime))
            {
                _logger.LogDebug("Extracted increment timestamp from line '{Line}': {Time}", trimmed, extractedTime);
                increments.Add(new IncrementEntryDto(
                    extractedTime,
                    "backup",
                    null,
                    null
                ));
                continue;
            }

            _logger.LogDebug("Could not parse increment line: {Line}", trimmed);
        }

        return increments.OrderByDescending(i => i.Time);
    }

    private IEnumerable<IncrementEntryDto> ParseIncrementSizes(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var increments = new List<IncrementEntryDto>();
        long cumulativeSize = 0;

        foreach (var line in lines)
        {
            // Parse increment size format
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                var timestamp = parts[0];
                if (DateTimeOffset.TryParse(timestamp, out var time) && long.TryParse(parts[1], out var size))
                {
                    cumulativeSize += size;
                    increments.Add(new IncrementEntryDto(
                        time,
                        "backup",
                        size,
                        cumulativeSize
                    ));
                }
            }
        }

        return increments.OrderByDescending(i => i.Time);
    }
}
