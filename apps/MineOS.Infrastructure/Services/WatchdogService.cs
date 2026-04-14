using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class WatchdogService : BackgroundService, IWatchdogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HostOptions _options;
    private readonly ILogger<WatchdogService> _logger;
    private readonly IRepository<CrashEvent> _crashEventRepo;
    private readonly IRepository<SystemNotification> _notificationRepo;
    private readonly ConcurrentDictionary<string, ServerMonitorState> _serverStates = new();
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);

    public WatchdogService(
        IServiceScopeFactory scopeFactory,
        IOptions<HostOptions> options,
        ILogger<WatchdogService> logger,
        IRepository<CrashEvent> crashEventRepo,
        IRepository<SystemNotification> notificationRepo)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _crashEventRepo = crashEventRepo;
        _notificationRepo = notificationRepo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Watchdog service starting");

        // Initial delay to let other services start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllServersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in watchdog monitoring loop");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Watchdog service stopping");
    }

    private async Task CheckAllServersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var serverService = scope.ServiceProvider.GetRequiredService<IServerService>();
        var processManager = scope.ServiceProvider.GetRequiredService<IProcessManager>();
        var activityService = scope.ServiceProvider.GetRequiredService<IPlayerActivityService>();

        // Get all servers
        var servers = await serverService.ListServersAsync(cancellationToken);

        foreach (var server in servers)
        {
            try
            {
                await CheckServerAsync(server.Name, serverService, processManager, activityService, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking server {ServerName}", server.Name);
            }
        }

        // Clean up states for servers that no longer exist
        var currentServerNames = servers.Select(s => s.Name).ToHashSet();
        foreach (var name in _serverStates.Keys.Where(n => !currentServerNames.Contains(n)).ToList())
        {
            _serverStates.TryRemove(name, out _);
        }
    }

    private async Task CheckServerAsync(
        string serverName,
        IServerService serverService,
        IProcessManager processManager,
        IPlayerActivityService activityService,
        CancellationToken cancellationToken)
    {
        var config = await serverService.GetServerConfigAsync(serverName, cancellationToken);
        var autoRestart = config.AutoRestart;

        // Get or create state for this server
        var state = _serverStates.GetOrAdd(serverName, _ => new ServerMonitorState(serverName));
        state.IsMonitoring = autoRestart.Enabled;

        // Check if process is running
        var isRunning = await processManager.IsServerRunningAsync(serverName, cancellationToken);

        // Process player activity logs for running servers
        if (isRunning)
        {
            try
            {
                await activityService.ProcessServerLogsAsync(serverName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error processing activity logs for server {ServerName}", serverName);
            }
        }

        // Close open sessions when server stops
        if (state.WasRunning && !isRunning)
        {
            try
            {
                await activityService.CloseOpenSessionsAsync(serverName, "server_stop", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing sessions for server {ServerName}", serverName);
            }
        }

        var stopRequested = HasStopRequestedFlag(serverName);
        if (state.WasRunning && !isRunning && stopRequested)
        {
            _logger.LogInformation("Server {ServerName} stopped manually; skipping auto-restart", serverName);
            state.RestartAttempts = 0;
            state.LastCrashTime = null;
            state.LastRestartAttempt = null;
            state.CooldownEndsAt = null;
            state.LastManualStopTime = DateTimeOffset.UtcNow;
            state.WasRunning = false;
            ClearStopRequestedFlag(serverName);
            return;
        }

        if (isRunning && state.LastManualStopTime.HasValue)
        {
            state.LastManualStopTime = null;
        }

        if (!autoRestart.Enabled)
        {
            // Reset state when disabled
            state.RestartAttempts = 0;
            state.LastCrashTime = null;
            state.WasRunning = isRunning;
            return;
        }

        // Detect crash: was running, now not running
        if (state.WasRunning && !isRunning)
        {
            _logger.LogWarning("Crash detected for server {ServerName}", serverName);
            await HandleCrashAsync(serverName, config, state, serverService, cancellationToken);
        }

        // Reset attempt counter after stability period
        if (isRunning && state.RestartAttempts > 0)
        {
            var stableTime = state.LastRestartAttempt.HasValue
                ? DateTimeOffset.UtcNow - state.LastRestartAttempt.Value
                : TimeSpan.MaxValue;

            if (stableTime.TotalMinutes >= autoRestart.AttemptResetMinutes)
            {
                _logger.LogInformation("Server {ServerName} stable for {Minutes} minutes, resetting restart counter",
                    serverName, autoRestart.AttemptResetMinutes);
                state.RestartAttempts = 0;
            }
        }

        state.WasRunning = isRunning;
    }

    private async Task HandleCrashAsync(
        string serverName,
        ServerConfigDto config,
        ServerMonitorState state,
        IServerService serverService,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var autoRestart = config.AutoRestart;

        state.LastCrashTime = now;

        // Detect crash type (could be enhanced with log analysis)
        var crashType = await DetectCrashTypeAsync(serverName, serverService, cancellationToken);

        // Record crash event
        await RecordCrashEventAsync(serverName, crashType, null, cancellationToken);

        // Send crash notification if enabled
        if (autoRestart.NotifyOnCrash)
        {
            await SendNotificationAsync(
                serverName,
                "error",
                "Server Crashed",
                $"Server '{serverName}' has crashed ({crashType}). Auto-restart is {(autoRestart.Enabled ? "enabled" : "disabled")}.",
                cancellationToken);
        }

        // Check if we should attempt restart
        if (!ShouldAttemptRestart(state, autoRestart))
        {
            _logger.LogWarning("Not attempting restart for {ServerName}: max attempts reached or in cooldown", serverName);
            return;
        }

        // Check cooldown
        if (state.CooldownEndsAt.HasValue && now < state.CooldownEndsAt.Value)
        {
            _logger.LogInformation("Server {ServerName} in cooldown until {CooldownEnds}",
                serverName, state.CooldownEndsAt.Value);
            return;
        }

        // Attempt restart
        state.RestartAttempts++;
        state.LastRestartAttempt = now;
        state.CooldownEndsAt = now.AddSeconds(autoRestart.CooldownSeconds);

        _logger.LogInformation("Attempting auto-restart for {ServerName} (attempt {Attempt}/{Max})",
            serverName, state.RestartAttempts, autoRestart.MaxAttempts == 0 ? "unlimited" : autoRestart.MaxAttempts.ToString());

        var success = await AttemptRestartAsync(serverName, serverService, cancellationToken);

        // Update crash event with restart result
        await UpdateCrashEventRestartResultAsync(serverName, success, cancellationToken);

        if (autoRestart.NotifyOnRestart)
        {
            var message = success
                ? $"Server '{serverName}' has been automatically restarted (attempt {state.RestartAttempts})."
                : $"Failed to auto-restart server '{serverName}' (attempt {state.RestartAttempts}).";

            await SendNotificationAsync(
                serverName,
                success ? "success" : "error",
                success ? "Server Restarted" : "Restart Failed",
                message,
                cancellationToken);
        }
    }

    private async Task<string> DetectCrashTypeAsync(
        string serverName,
        IServerService serverService,
        CancellationToken cancellationToken)
    {
        // Check for crash reports
        try
        {
            var serverPath = Path.Combine("/var/games/minecraft/servers", serverName);
            var crashReportsDir = Path.Combine(serverPath, "crash-reports");

            if (Directory.Exists(crashReportsDir))
            {
                var recentCrashReport = Directory.GetFiles(crashReportsDir, "*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (recentCrashReport != null &&
                    (DateTimeOffset.UtcNow - recentCrashReport.LastWriteTimeUtc).TotalMinutes < 1)
                {
                    // Check for OOM in crash report
                    var content = await File.ReadAllTextAsync(recentCrashReport.FullName, cancellationToken);
                    if (content.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase))
                    {
                        return "OutOfMemory";
                    }
                    return "CrashReport";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking crash reports for {ServerName}", serverName);
        }

        return "ProcessDeath";
    }

    private bool ShouldAttemptRestart(ServerMonitorState state, AutoRestartConfigDto config)
    {
        // MaxAttempts of 0 means unlimited
        if (config.MaxAttempts > 0 && state.RestartAttempts >= config.MaxAttempts)
        {
            return false;
        }

        return true;
    }

    private async Task<bool> AttemptRestartAsync(
        string serverName,
        IServerService serverService,
        CancellationToken cancellationToken)
    {
        try
        {
            // Small delay before restart to let things settle
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            await serverService.StartServerAsync(serverName, cancellationToken);

            // Wait a bit and verify it started
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            using var scope = _scopeFactory.CreateScope();
            var processManager = scope.ServiceProvider.GetRequiredService<IProcessManager>();
            var isRunning = await processManager.IsServerRunningAsync(serverName, cancellationToken);

            if (isRunning)
            {
                _logger.LogInformation("Successfully restarted server {ServerName}", serverName);
                return true;
            }

            _logger.LogWarning("Server {ServerName} restart command succeeded but server is not running", serverName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart server {ServerName}", serverName);
            return false;
        }
    }

    private async Task RecordCrashEventAsync(
        string serverName,
        string crashType,
        string? details,
        CancellationToken cancellationToken)
    {
        try
        {
            await _crashEventRepo.AddAsync(new CrashEvent
            {
                ServerName = serverName,
                DetectedAt = DateTimeOffset.UtcNow,
                CrashType = crashType,
                CrashDetails = details,
                AutoRestartAttempted = false,
                AutoRestartSucceeded = false
            }, cancellationToken);

            // Report crash telemetry (fire-and-forget to avoid delaying crash handling)
            using var scope = _scopeFactory.CreateScope();
            var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
            _ = telemetryService.ReportLifecycleEventAsync("crash", new
            {
                crash_type = crashType
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record crash event for {ServerName}", serverName);
        }
    }

    private async Task UpdateCrashEventRestartResultAsync(
        string serverName,
        bool success,
        CancellationToken cancellationToken)
    {
        try
        {
            var events = await _crashEventRepo.ToListAsync(
                c => c.ServerName == serverName, cancellationToken);

            var latestCrash = events.OrderByDescending(c => c.DetectedAt).FirstOrDefault();
            if (latestCrash != null)
            {
                latestCrash.AutoRestartAttempted = true;
                latestCrash.AutoRestartSucceeded = success;
                latestCrash.RestartAttemptedAt = DateTimeOffset.UtcNow;
                await _crashEventRepo.UpdateAsync(latestCrash, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update crash event restart result for {ServerName}", serverName);
        }
    }

    private async Task SendNotificationAsync(
        string serverName,
        string type,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _notificationRepo.AddAsync(new SystemNotification
            {
                Type = type,
                Title = title,
                Message = message,
                ServerName = serverName,
                CreatedAt = DateTimeOffset.UtcNow,
                IsRead = false
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for {ServerName}", serverName);
        }
    }

    // IWatchdogService implementation

    public async Task<IEnumerable<CrashEventDto>> GetCrashEventsAsync(
        string serverName,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var events = await _crashEventRepo.ToListAsync(
            c => c.ServerName == serverName, cancellationToken);

        return events
            .OrderByDescending(c => c.DetectedAt)
            .Take(limit)
            .Select(c => new CrashEventDto(
                c.Id, c.ServerName, c.DetectedAt, c.CrashType,
                c.CrashDetails, c.AutoRestartAttempted, c.AutoRestartSucceeded))
            .ToList();
    }

    public async Task<IEnumerable<CrashEventDto>> GetAllCrashEventsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var events = await _crashEventRepo.ToListAsync(_ => true, cancellationToken);

        return events
            .OrderByDescending(c => c.DetectedAt)
            .Take(limit)
            .Select(c => new CrashEventDto(
                c.Id, c.ServerName, c.DetectedAt, c.CrashType,
                c.CrashDetails, c.AutoRestartAttempted, c.AutoRestartSucceeded))
            .ToList();
    }

    public async Task ClearCrashHistoryAsync(string serverName, CancellationToken cancellationToken = default)
    {
        await _crashEventRepo.RemoveWhereAsync(
            c => c.ServerName == serverName, cancellationToken);

        if (_serverStates.TryGetValue(serverName, out var state))
        {
            state.ResetCrashHistory();
        }
    }

    public Dictionary<string, ServerWatchdogStatus> GetWatchdogStatus()
    {
        return _serverStates.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToStatus());
    }

    private class ServerMonitorState
    {
        private readonly object _lock = new();
        public string ServerName { get; }
        public bool IsMonitoring { get; set; }
        public bool WasRunning { get; set; }
        public int RestartAttempts { get; set; }
        public DateTimeOffset? LastCrashTime { get; set; }
        public DateTimeOffset? LastManualStopTime { get; set; }
        public DateTimeOffset? LastRestartAttempt { get; set; }
        public DateTimeOffset? CooldownEndsAt { get; set; }

        public ServerMonitorState(string serverName)
        {
            ServerName = serverName;
        }

        public void ResetCrashHistory()
        {
            lock (_lock)
            {
                RestartAttempts = 0;
                LastCrashTime = null;
                LastRestartAttempt = null;
                CooldownEndsAt = null;
            }
        }

        public ServerWatchdogStatus ToStatus()
        {
            lock (_lock)
            {
                return new ServerWatchdogStatus(
                    ServerName, IsMonitoring, WasRunning, RestartAttempts,
                    LastCrashTime, LastManualStopTime, LastRestartAttempt, CooldownEndsAt);
            }
        }
    }

    private string GetServerPath(string name) =>
        Path.Combine(_options.BaseDirectory, _options.ServersPathSegment, name);

    private string GetStopRequestedFlagPath(string name) =>
        Path.Combine(GetServerPath(name), ".mineos-stop-requested");

    private bool HasStopRequestedFlag(string name) =>
        File.Exists(GetStopRequestedFlagPath(name));

    private void ClearStopRequestedFlag(string name)
    {
        var flagPath = GetStopRequestedFlagPath(name);
        if (File.Exists(flagPath))
        {
            try
            {
                File.Delete(flagPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to clear stop requested flag for {ServerName}", name);
            }
        }
    }
}
