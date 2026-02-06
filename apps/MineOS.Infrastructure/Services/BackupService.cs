using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Infrastructure.Services;

public sealed class BackupService : IBackupService
{
    private readonly ILogger<BackupService> _logger;
    private readonly HostOptions _hostOptions;
    private readonly AppDbContext _db;
    private readonly IFeatureUsageTracker _featureTracker;
    private readonly ITelemetryService _telemetryService;

    public BackupService(
        ILogger<BackupService> logger,
        IOptions<HostOptions> hostOptions,
        AppDbContext db,
        IFeatureUsageTracker featureTracker,
        ITelemetryService telemetryService)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
        _db = db;
        _featureTracker = featureTracker;
        _telemetryService = telemetryService;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetBackupPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.BackupsPathSegment, serverName);

    public async Task<IEnumerable<IncrementEntryDto>> ListBackupsAsync(string serverName, CancellationToken cancellationToken)
    {
        var jobBackups = await _db.Jobs.AsNoTracking()
            .Where(j => j.Type == "backup" && j.ServerName == serverName && j.Status == "completed")
            .ToListAsync(cancellationToken);

        var jobEntries = jobBackups
            .Select(j => new IncrementEntryDto(
                j.CompletedAt ?? j.StartedAt,
                "backup",
                null,
                null))
            .OrderByDescending(j => j.Time)
            .ToList();

        var sizeEntries = (await GetBackupSizesAsync(serverName, cancellationToken)).ToList();
        if (jobEntries.Count > 0)
        {
            return MergeIncrementSizes(jobEntries, sizeEntries);
        }

        var rdiffEntries = (await ListRdiffIncrementsAsync(serverName, cancellationToken)).ToList();
        return MergeIncrementSizes(rdiffEntries, sizeEntries);
    }

    public async Task CreateBackupAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            var serverPath = GetServerPath(serverName);
            var backupPath = GetBackupPath(serverName);

            if (!Directory.Exists(serverPath))
            {
                throw new DirectoryNotFoundException($"Server '{serverName}' not found");
            }

            _logger.LogInformation("Creating backup for server {ServerName} at {BackupPath}", serverName, backupPath);

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
            _featureTracker.Increment("backups_created");
        }
        catch (Exception ex)
        {
            await _telemetryService.ReportErrorAsync(
                "BACKUP_FAILED",
                ex.Message,
                ex.StackTrace,
                "high",
                serverName,
                cancellationToken);
            throw;
        }
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
        if (keepCount <= 0)
        {
            return;
        }

        var jobBackups = await _db.Jobs
            .Where(j => j.Type == "backup" && j.ServerName == serverName && j.Status == "completed")
            .ToListAsync(cancellationToken);

        var orderedJobs = jobBackups
            .Select(j => new { Job = j, Time = j.CompletedAt ?? j.StartedAt })
            .OrderByDescending(j => j.Time)
            .ToList();

        if (orderedJobs.Count > keepCount)
        {
            var thresholdTime = orderedJobs[keepCount - 1].Time;
            var toRemove = orderedJobs.Skip(keepCount).Select(j => j.Job).ToList();

            if (toRemove.Count > 0)
            {
                _db.Jobs.RemoveRange(toRemove);
                await _db.SaveChangesAsync(cancellationToken);
            }

            await PruneRdiffAsync(serverName, thresholdTime, cancellationToken);
            _logger.LogInformation("Pruned backups for server {ServerName}, keeping {KeepCount}", serverName, keepCount);
            return;
        }

        var backupPath = GetBackupPath(serverName);

        if (!Directory.Exists(backupPath))
        {
            return;
        }

        var increments = (await ListRdiffIncrementsAsync(serverName, cancellationToken)).ToList();
        if (increments.Count <= keepCount)
        {
            _logger.LogInformation("No backups to prune for server {ServerName}; count={Count}, keep={KeepCount}",
                serverName, increments.Count, keepCount);
            return;
        }

        var rdiffThresholdTime = increments
            .OrderByDescending(i => i.Time)
            .ElementAt(keepCount - 1)
            .Time;

        await PruneRdiffAsync(serverName, rdiffThresholdTime, cancellationToken);
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

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("Found ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trimmed.StartsWith("Current mirror:", StringComparison.OrdinalIgnoreCase))
            {
                var mirrorText = trimmed["Current mirror:".Length..].Trim();
                if (TryParseRdiffTimestamp(mirrorText, out var mirrorTime))
                {
                    _logger.LogDebug("Parsed current mirror timestamp: {Time}", mirrorTime);
                    increments.Add(new IncrementEntryDto(mirrorTime, "backup", null, null));
                }
                else
                {
                    _logger.LogDebug("Could not parse current mirror line: {Line}", trimmed);
                }
                continue;
            }

            if (TryParseRdiffTimestamp(trimmed, out var time))
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

            _logger.LogDebug("Could not parse increment line: {Line}", trimmed);
        }

        return increments.OrderByDescending(i => i.Time);
    }

    private static bool TryParseRdiffTimestamp(string input, out DateTimeOffset time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var isoMatch = Regex.Match(input,
            @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[+-]\d{2}:\d{2}|Z))");
        if (isoMatch.Success)
        {
            var isoValue = isoMatch.Groups[1].Value;
            var isoFormats = new[] { "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.fffffffK" };
            if (DateTimeOffset.TryParseExact(isoValue, isoFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out time))
            {
                return true;
            }
        }

        var englishMatch = Regex.Match(input,
            @"\b(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\s+[A-Za-z]{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2}\s+\d{4}\b");
        if (englishMatch.Success)
        {
            var englishValue = englishMatch.Value;
            var englishFormats = new[] { "ddd MMM d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy" };
            if (DateTimeOffset.TryParseExact(englishValue, englishFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out time))
            {
                return true;
            }
        }

        return DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out time);
    }

    private IEnumerable<IncrementEntryDto> ParseIncrementSizes(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var increments = new List<IncrementEntryDto>();
        long cumulativeSize = 0;
        const string isoPattern = @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[+-]\d{2}:\d{2}|Z))";
        const string englishPattern = @"\b(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\s+[A-Za-z]{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2}\s+\d{4}\b";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("Found ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isoMatch = Regex.Match(trimmed, isoPattern);
            var englishMatch = isoMatch.Success ? Match.Empty : Regex.Match(trimmed, englishPattern);
            var match = isoMatch.Success ? isoMatch : englishMatch;
            if (!match.Success)
            {
                continue;
            }

            if (!TryParseRdiffTimestamp(match.Value, out var time))
            {
                continue;
            }

            var remainder = trimmed[(match.Index + match.Length)..];
            var sizeMatch = Regex.Match(remainder, @"\b(\d+)\b");
            if (!sizeMatch.Success || !long.TryParse(sizeMatch.Groups[1].Value, out var size))
            {
                continue;
            }

            cumulativeSize += size;
            increments.Add(new IncrementEntryDto(
                time,
                "backup",
                size,
                cumulativeSize
            ));
        }

        return increments.OrderByDescending(i => i.Time);
    }

    private static IReadOnlyList<IncrementEntryDto> MergeIncrementSizes(
        IReadOnlyList<IncrementEntryDto> entries,
        IReadOnlyList<IncrementEntryDto> sizeEntries)
    {
        if (entries.Count == 0 || sizeEntries.Count == 0)
        {
            return entries;
        }

        const string keyFormat = "yyyy-MM-dd'T'HH:mm:ss";
        var sizeBySecond = sizeEntries
            .GroupBy(entry => entry.Time.ToUniversalTime().ToString(keyFormat, CultureInfo.InvariantCulture))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(e => e.Time).First());

        var merged = new List<IncrementEntryDto>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.Size is not null && entry.CumulativeSize is not null)
            {
                merged.Add(entry);
                continue;
            }

            var key = entry.Time.ToUniversalTime().ToString(keyFormat, CultureInfo.InvariantCulture);
            if (!sizeBySecond.TryGetValue(key, out var sizeEntry))
            {
                sizeEntry = FindClosestSizeEntry(entry.Time, sizeEntries, TimeSpan.FromMinutes(2));
            }

            if (sizeEntry is null)
            {
                merged.Add(entry);
                continue;
            }

            merged.Add(entry with
            {
                Size = entry.Size ?? sizeEntry.Size,
                CumulativeSize = entry.CumulativeSize ?? sizeEntry.CumulativeSize
            });
        }

        return merged;
    }

    private static IncrementEntryDto? FindClosestSizeEntry(
        DateTimeOffset target,
        IReadOnlyList<IncrementEntryDto> sizeEntries,
        TimeSpan tolerance)
    {
        IncrementEntryDto? closest = null;
        var closestDiff = TimeSpan.MaxValue;
        foreach (var entry in sizeEntries)
        {
            var diff = (entry.Time - target).Duration();
            if (diff > tolerance || diff >= closestDiff)
            {
                continue;
            }

            closest = entry;
            closestDiff = diff;
        }

        return closest;
    }

    private async Task PruneRdiffAsync(string serverName, DateTimeOffset thresholdTime, CancellationToken cancellationToken)
    {
        var backupPath = GetBackupPath(serverName);
        if (!Directory.Exists(backupPath))
        {
            return;
        }

        var threshold = thresholdTime.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var psi = new ProcessStartInfo
        {
            FileName = "rdiff-backup",
            Arguments = $"remove increments --older-than {threshold} \"{backupPath}\"",
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

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(error) &&
                error.Contains("older than", StringComparison.OrdinalIgnoreCase) &&
                error.Contains("no", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("No rdiff-backup increments older than cutoff for server {ServerName}", serverName);
                return;
            }

            throw new InvalidOperationException($"Prune failed: {error}");
        }
    }

    private async Task<IReadOnlyList<IncrementEntryDto>> ListRdiffIncrementsAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        var backupPath = GetBackupPath(serverName);
        if (!Directory.Exists(backupPath))
        {
            return Array.Empty<IncrementEntryDto>();
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
            return Array.Empty<IncrementEntryDto>();
        }

        _logger.LogInformation("rdiff-backup list output for {ServerName}: {Output}", serverName, output);
        var increments = ParseIncrementsV2(output).ToList();
        _logger.LogInformation("Parsed {Count} increments for {ServerName}", increments.Count, serverName);
        return increments;
    }
}
