using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly AppDbContext _db;
    private readonly IMonitoringService _monitoringService;
    private readonly IProcessManager _processManager;
    private readonly HostOptions _hostOptions;
    private readonly ILogger<PerformanceService> _logger;

    public PerformanceService(
        AppDbContext db,
        IMonitoringService monitoringService,
        IProcessManager processManager,
        IOptions<HostOptions> hostOptions,
        ILogger<PerformanceService> logger)
    {
        _db = db;
        _monitoringService = monitoringService;
        _processManager = processManager;
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

        return new PerformanceSampleDto(
            serverName,
            timestamp,
            isRunning,
            cpuPercent,
            usedMb,
            totalMb,
            null,
            players);
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
