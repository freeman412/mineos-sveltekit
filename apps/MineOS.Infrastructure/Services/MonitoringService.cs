using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed partial class MonitoringService : IMonitoringService
{
    private readonly ILogger<MonitoringService> _logger;
    private readonly IProcessManager _processManager;
    private readonly HostOptions _hostOptions;

    public MonitoringService(
        ILogger<MonitoringService> logger,
        IProcessManager processManager,
        IOptions<HostOptions> hostOptions)
    {
        _logger = logger;
        _processManager = processManager;
        _hostOptions = hostOptions.Value;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetPropertiesPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "server.properties");

    public async IAsyncEnumerable<ServerHeartbeatDto> StreamHeartbeatAsync(
        string serverName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var processInfo = _processManager.GetServerProcess(serverName);
            var isRunning = await _processManager.IsServerRunningAsync(serverName, cancellationToken);
            var ping = await GetPingInfoAsync(serverName, cancellationToken);
            var memory = await GetMemoryInfoAsync(serverName, cancellationToken);

            yield return new ServerHeartbeatDto(
                serverName,
                isRunning ? "up" : "down",
                processInfo?.JavaPid,
                processInfo?.ScreenPid,
                ping,
                memory.ResidentMemory
            );

            await Task.Delay(2000, cancellationToken); // Update every 2 seconds
        }
    }

    public async Task<PingInfoDto?> GetPingInfoAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            // Get server port from server.properties
            var properties = await ReadPropertiesAsync(serverName, cancellationToken);
            if (!properties.TryGetValue("server-port", out var portStr) || !int.TryParse(portStr, out var port))
            {
                port = 25565; // Default Minecraft port
            }

            // Use mcstatus (if available) or implement basic ping protocol
            // For now, return null to indicate offline/unavailable
            // TODO: Implement Minecraft ping protocol
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ping server {ServerName}", serverName);
            return null;
        }
    }

    public async Task<QueryInfoDto?> GetQueryInfoAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            // Get query port from server.properties
            var properties = await ReadPropertiesAsync(serverName, cancellationToken);
            if (!properties.TryGetValue("enable-query", out var enableQuery) || enableQuery != "true")
            {
                return null; // Query not enabled
            }

            // TODO: Implement Minecraft query protocol
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query server {ServerName}", serverName);
            return null;
        }
    }

    public async Task<DetailedMemoryInfoDto> GetMemoryInfoAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            var processInfo = _processManager.GetServerProcess(serverName);

            if (processInfo?.JavaPid == null)
            {
                return new DetailedMemoryInfoDto(0, 0, 0);
            }

            // Read /proc/{pid}/status for memory info
            var statusPath = $"/proc/{processInfo.JavaPid}/status";
            if (!File.Exists(statusPath))
            {
                return new DetailedMemoryInfoDto(0, 0, 0);
            }

            var statusContent = await File.ReadAllTextAsync(statusPath, cancellationToken);

            long vmSize = 0, vmRss = 0, rssFile = 0;

            foreach (var line in statusContent.Split('\n'))
            {
                if (line.StartsWith("VmSize:"))
                {
                    vmSize = ParseMemoryLine(line);
                }
                else if (line.StartsWith("VmRSS:"))
                {
                    vmRss = ParseMemoryLine(line);
                }
                else if (line.StartsWith("RssFile:"))
                {
                    rssFile = ParseMemoryLine(line);
                }
            }

            return new DetailedMemoryInfoDto(vmSize, vmRss, rssFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get memory info for server {ServerName}", serverName);
            return new DetailedMemoryInfoDto(0, 0, 0);
        }
    }

    private static long ParseMemoryLine(string line)
    {
        var match = MemoryRegex().Match(line);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var kb))
        {
            return kb * 1024; // Convert KB to bytes
        }
        return 0;
    }

    private async Task<Dictionary<string, string>> ReadPropertiesAsync(string serverName, CancellationToken cancellationToken)
    {
        var propertiesPath = GetPropertiesPath(serverName);
        var properties = new Dictionary<string, string>();

        if (!File.Exists(propertiesPath))
        {
            return properties;
        }

        var lines = await File.ReadAllLinesAsync(propertiesPath, cancellationToken);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                properties[parts[0].Trim()] = parts[1].Trim();
            }
        }

        return properties;
    }

    [GeneratedRegex(@":\s+(\d+)\s+kB")]
    private static partial Regex MemoryRegex();
}
