using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Infrastructure.Services;

public sealed class PerformanceService : IPerformanceService
{
    private static readonly ConcurrentDictionary<int, CpuSample> CpuSamples = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> TpsRequestTimes = new();
    private static readonly Regex TpsRegex = new(
        @"TPS from last 1m, 5m, 15m:\s*(?<one>[\d.]+),\s*(?<five>[\d.]+),\s*(?<fifteen>[\d.]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly AppDbContext _db;
    private readonly IMonitoringService _monitoringService;
    private readonly IProcessManager _processManager;
    private readonly IConsoleService _consoleService;
    private readonly HostOptions _hostOptions;
    private readonly ILogger<PerformanceService> _logger;

    public PerformanceService(
        AppDbContext db,
        IMonitoringService monitoringService,
        IProcessManager processManager,
        IConsoleService consoleService,
        IOptions<HostOptions> hostOptions,
        ILogger<PerformanceService> logger)
    {
        _db = db;
        _monitoringService = monitoringService;
        _processManager = processManager;
        _consoleService = consoleService;
        _hostOptions = hostOptions.Value;
        _logger = logger;
    }

    public async Task<PerformanceSampleDto> GetRealtimeAsync(string serverName, CancellationToken cancellationToken)
    {
        EnsureServerExists(serverName);
        var sample = await CaptureSampleAsync(serverName, cancellationToken);
        return sample ?? new PerformanceSampleDto(
            serverName,
            DateTimeOffset.UtcNow,
            false,
            0,
            0,
            0,
            null,
            0);
    }

    public async Task<IReadOnlyList<PerformanceSampleDto>> GetHistoryAsync(
        string serverName,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        EnsureServerExists(serverName);
        var cutoff = DateTimeOffset.UtcNow - window;

        return await _db.PerformanceMetrics.AsNoTracking()
            .Where(metric => metric.ServerName == serverName && metric.Timestamp >= cutoff)
            .OrderBy(metric => metric.Timestamp)
            .Select(metric => new PerformanceSampleDto(
                metric.ServerName,
                metric.Timestamp,
                true,
                metric.CpuPercent,
                metric.RamUsedMb,
                metric.RamTotalMb,
                metric.Tps,
                metric.PlayerCount))
            .ToListAsync(cancellationToken);
    }

    public async Task RecordSampleAsync(string serverName, CancellationToken cancellationToken)
    {
        EnsureServerExists(serverName);
        var processInfo = _processManager.GetServerProcess(serverName);
        if (processInfo?.JavaPid == null)
        {
            return;
        }

        var sample = await CaptureSampleAsync(serverName, cancellationToken, processInfo);
        if (sample == null || !sample.IsRunning)
        {
            return;
        }

        var entity = new PerformanceMetric
        {
            ServerName = sample.ServerName,
            Timestamp = sample.Timestamp,
            CpuPercent = sample.CpuPercent,
            RamUsedMb = sample.RamUsedMb,
            RamTotalMb = sample.RamTotalMb,
            Tps = sample.Tps,
            PlayerCount = sample.PlayerCount
        };

        _db.PerformanceMetrics.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        if (sample.Tps.HasValue)
        {
            await MaybeCreateLowTpsAlertAsync(serverName, sample.Tps.Value, cancellationToken);
        }
    }

    public async IAsyncEnumerable<PerformanceSampleDto> StreamRealtimeAsync(
        string serverName,
        TimeSpan interval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureServerExists(serverName);
        while (!cancellationToken.IsCancellationRequested)
        {
            PerformanceSampleDto sample;
            try
            {
                sample = await GetRealtimeAsync(serverName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture realtime performance sample for {ServerName}", serverName);
                sample = new PerformanceSampleDto(
                    serverName,
                    DateTimeOffset.UtcNow,
                    false,
                    0,
                    0,
                    0,
                    null,
                    0);
            }

            yield return sample;

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private async Task<PerformanceSampleDto?> CaptureSampleAsync(
        string serverName,
        CancellationToken cancellationToken,
        ServerProcessInfo? processInfo = null)
    {
        processInfo ??= _processManager.GetServerProcess(serverName);
        var hasJava = processInfo?.JavaPid != null;

        var timestamp = DateTimeOffset.UtcNow;
        var memoryTask = hasJava
            ? _monitoringService.GetMemoryInfoAsync(serverName, cancellationToken)
            : Task.FromResult(new DetailedMemoryInfoDto(0, 0, 0));
        var pingTask = _monitoringService.GetPingInfoAsync(serverName, cancellationToken);

        await Task.WhenAll(memoryTask, pingTask);

        var memory = memoryTask.Result;
        var ping = pingTask.Result;
        var isRunning = hasJava || processInfo?.ScreenPid != null || ping != null ||
                        HasRecentLogActivity(serverName, TimeSpan.FromMinutes(2));

        if (!isRunning)
        {
            return new PerformanceSampleDto(
                serverName,
                timestamp,
                false,
                0,
                0,
                0,
                null,
                0);
        }

        var cpuPercent = hasJava ? (TryGetCpuPercent(processInfo!.JavaPid!.Value, timestamp) ?? 0) : 0;
        var usedMb = hasJava ? memory.ResidentMemory / 1024 / 1024 : 0;
        var totalMb = hasJava ? memory.VirtualMemory / 1024 / 1024 : 0;
        var players = ping?.PlayersOnline ?? 0;
        var tps = await TryGetTpsAsync(serverName, isRunning, cancellationToken);

        return new PerformanceSampleDto(
            serverName,
            timestamp,
            isRunning,
            cpuPercent,
            usedMb,
            totalMb,
            tps,
            players);
    }

    private string GetLogPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName, "logs", "latest.log");

    private async Task<double?> TryGetTpsAsync(string serverName, bool isRunning, CancellationToken cancellationToken)
    {
        if (!isRunning)
        {
            return null;
        }

        var logPath = GetLogPath(serverName);
        var currentTps = TryParseTpsFromLog(logPath);

        var now = DateTimeOffset.UtcNow;
        var shouldRequest = !TpsRequestTimes.TryGetValue(serverName, out var lastRequest) ||
                            (now - lastRequest) > TimeSpan.FromMinutes(1);

        if (shouldRequest)
        {
            try
            {
                await _consoleService.SendCommandAsync(serverName, "/tps", cancellationToken);
                TpsRequestTimes[serverName] = now;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to request TPS for {ServerName}", serverName);
            }
        }

        return currentTps;
    }

    private static double? TryParseTpsFromLog(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return null;
        }

        foreach (var line in ReadLogTail(logPath, 200).Reverse())
        {
            var match = TpsRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (double.TryParse(
                    match.Groups["one"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var oneMinute))
            {
                return oneMinute;
            }
        }

        return null;
    }

    private static IEnumerable<string> ReadLogTail(string path, int maxLines)
    {
        try
        {
            return File.ReadLines(path).Reverse().Take(maxLines).Reverse().ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private async Task MaybeCreateLowTpsAlertAsync(string serverName, double tps, CancellationToken cancellationToken)
    {
        if (tps >= 18)
        {
            return;
        }

        var exists = await _db.SystemNotifications
            .AsNoTracking()
            .AnyAsync(
                notification => notification.ServerName == serverName
                                && notification.Title == "Low TPS"
                                && notification.DismissedAt == null,
                cancellationToken);

        if (exists)
        {
            return;
        }

        var notification = new SystemNotification
        {
            Type = "warning",
            Title = "Low TPS",
            Message = $"TPS dropped to {tps:0.0} for {serverName}.",
            CreatedAt = DateTimeOffset.UtcNow,
            ServerName = serverName,
            IsRead = false
        };

        _db.SystemNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<SparkStatusDto> GetSparkStatusAsync(string serverName, CancellationToken cancellationToken)
    {
        EnsureServerExists(serverName);
        var serverPath = Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);
        var pluginDir = Path.Combine(serverPath, "plugins");
        var modDir = Path.Combine(serverPath, "mods");

        var jarPath = FindSparkJar(pluginDir) ?? FindSparkJar(modDir);
        if (jarPath == null)
        {
            return Task.FromResult(new SparkStatusDto(false, null, null, null, 0, Array.Empty<string>()));
        }

        var mode = jarPath.StartsWith(pluginDir, StringComparison.OrdinalIgnoreCase) ? "plugin" : "mod";
        var jarName = Path.GetFileName(jarPath);
        var version = TryExtractSparkVersion(jarName);
        var reports = GetSparkReports(serverPath, mode);

        return Task.FromResult(new SparkStatusDto(true, mode, jarName, version, reports.Count, reports));
    }

    private static string? FindSparkJar(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith("spark", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryExtractSparkVersion(string jarName)
    {
        var match = Regex.Match(jarName, @"spark[-_]?([0-9][\w\.\-]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var version = match.Groups[1].Value;
        return version.Replace(".jar", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetSparkReports(string serverPath, string mode)
    {
        var reportDirectories = new List<string>();
        if (mode == "plugin")
        {
            reportDirectories.Add(Path.Combine(serverPath, "plugins", "spark"));
        }
        else
        {
            reportDirectories.Add(Path.Combine(serverPath, "config", "spark"));
        }

        var reports = new List<string>();
        foreach (var directory in reportDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var files = Directory
                .EnumerateFiles(directory, "*.html", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!);

            reports.AddRange(files!);
        }

        return reports;
    }

    private void EnsureServerExists(string serverName)
    {
        var path = Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }
    }

    private bool HasRecentLogActivity(string serverName, TimeSpan window)
    {
        try
        {
            var logPath = Path.Combine(
                _hostOptions.BaseDirectory,
                _hostOptions.ServersPathSegment,
                serverName,
                "logs",
                "latest.log");
            if (!File.Exists(logPath))
            {
                return false;
            }

            var lastWrite = File.GetLastWriteTimeUtc(logPath);
            if (lastWrite == DateTime.MinValue)
            {
                return false;
            }

            return DateTimeOffset.UtcNow - lastWrite <= window;
        }
        catch
        {
            return false;
        }
    }

    private static double? TryGetCpuPercent(int pid, DateTimeOffset timestamp)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            var totalTime = process.TotalProcessorTime;
            var sample = CpuSamples.AddOrUpdate(
                pid,
                _ => new CpuSample(totalTime, timestamp),
                (_, existing) => existing);

            var elapsedMs = (timestamp - sample.Timestamp).TotalMilliseconds;
            if (elapsedMs <= 0)
            {
                CpuSamples[pid] = new CpuSample(totalTime, timestamp);
                return null;
            }

            var cpuDeltaMs = (totalTime - sample.TotalProcessorTime).TotalMilliseconds;
            CpuSamples[pid] = new CpuSample(totalTime, timestamp);

            var percent = cpuDeltaMs / (elapsedMs * Environment.ProcessorCount) * 100;
            if (double.IsNaN(percent) || double.IsInfinity(percent))
            {
                return null;
            }

            return Math.Clamp(percent, 0, 100);
        }
        catch
        {
            CpuSamples.TryRemove(pid, out _);
            return null;
        }
    }

    private readonly record struct CpuSample(TimeSpan TotalProcessorTime, DateTimeOffset Timestamp);
}
