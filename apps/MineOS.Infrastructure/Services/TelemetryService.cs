using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public class TelemetryService : ITelemetryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<TelemetryService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServerService _serverService;
    private readonly IUserService _userService;
    private readonly IBackupService _backupService;
    private readonly IWorldService _worldService;
    private readonly HttpClient _httpClient;
    private readonly bool _enabled;
    private readonly string _endpoint;
    private readonly string _installationId;

    public TelemetryService(
        ILogger<TelemetryService> logger,
        IConfiguration configuration,
        IServerService serverService,
        IUserService userService,
        IBackupService backupService,
        IWorldService worldService,
        HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _serverService = serverService;
        _userService = userService;
        _backupService = backupService;
        _worldService = worldService;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);

        // Read telemetry configuration
        _enabled = configuration["MINEOS_TELEMETRY_ENABLED"]?.ToLowerInvariant() != "false";
        _endpoint = configuration["MINEOS_TELEMETRY_ENDPOINT"] ?? "https://mineos.net";
        _installationId = configuration["MINEOS_INSTALLATION_ID"] ?? string.Empty;
    }

    public async Task ReportUsageAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Usage telemetry disabled, skipping report");
            return;
        }

        if (string.IsNullOrEmpty(_installationId))
        {
            _logger.LogWarning("Usage telemetry skipped: MINEOS_INSTALLATION_ID is not set");
            return;
        }

        try
        {
            // Gather usage statistics
            var servers = await _serverService.ListServersAsync(cancellationToken);
            var users = await _userService.ListUsersAsync(cancellationToken);

            var serverCount = servers.Count;
            var activeServerCount = 0;
            var totalBackupsCount = 0;
            var totalWorldsCount = 0;
            var serversWithModsCount = 0;
            var serversWithPluginsCount = 0;

            foreach (var server in servers)
            {
                try
                {
                    var status = await _serverService.GetServerStatusAsync(server.Name, cancellationToken);
                    if (status.Status == "up")
                    {
                        activeServerCount++;
                    }

                    // Count backups for this server
                    var backups = await _backupService.ListBackupsAsync(server.Name, cancellationToken);
                    totalBackupsCount += backups.Count();

                    // Count worlds for this server
                    var worlds = await _worldService.ListWorldsAsync(server.Name, cancellationToken);
                    totalWorldsCount += worlds.Count;

                    // Check for mods directory
                    var serverPath = Path.Combine(_configuration["Host:BaseDirectory"] ?? "/var/games/minecraft",
                        _configuration["Host:ServersPathSegment"] ?? "servers", server.Name);
                    if (Directory.Exists(Path.Combine(serverPath, "mods")) &&
                        Directory.GetFiles(Path.Combine(serverPath, "mods"), "*.jar").Length > 0)
                    {
                        serversWithModsCount++;
                    }

                    // Check for plugins directory
                    if (Directory.Exists(Path.Combine(serverPath, "plugins")) &&
                        Directory.GetFiles(Path.Combine(serverPath, "plugins"), "*.jar").Length > 0)
                    {
                        serversWithPluginsCount++;
                    }
                }
                catch
                {
                    // Ignore errors for individual servers
                }
            }

            var usageEvent = new UsageEvent
            {
                InstallationId = _installationId,
                ServerCount = serverCount,
                ActiveServerCount = activeServerCount,
                TotalUserCount = users.Count,
                ActiveUserCount = users.Count(u => true), // All users considered active for now
                MineOSVersion = GetMineOSVersion(),
                TotalBackupsCount = totalBackupsCount,
                TotalWorldsCount = totalWorldsCount,
                ServersWithModsCount = serversWithModsCount,
                ServersWithPluginsCount = serversWithPluginsCount
            };

            await SendTelemetryAsync(usageEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send usage telemetry");
        }
    }

    public async Task ReportLifecycleEventAsync(string eventType, object? metadata = null, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        if (string.IsNullOrEmpty(_installationId))
        {
            return;
        }

        try
        {
            var lifecycleEvent = new LifecycleEvent
            {
                InstallationId = _installationId,
                EventType = eventType,
                MineOSVersion = GetMineOSVersion(),
                Timestamp = DateTime.UtcNow,
                Metadata = metadata
            };

            await SendLifecycleEventAsync(lifecycleEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send lifecycle event {EventType}", eventType);
        }
    }

    private async Task SendTelemetryAsync(UsageEvent usageEvent, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(usageEvent, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{_endpoint}/api/telemetry/usage";

            _logger.LogInformation("Posting usage telemetry to {Url} for installation {Id}",
                url, _installationId[..8] + "...");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Usage telemetry reported successfully");
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Usage telemetry request failed: {StatusCode} {Body}",
                    response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send usage telemetry request");
        }
    }

    private async Task SendLifecycleEventAsync(LifecycleEvent lifecycleEvent, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(lifecycleEvent, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"{_endpoint}/api/telemetry/event";

            _logger.LogInformation("Posting lifecycle event {EventType} to {Url} for installation {Id}",
                lifecycleEvent.EventType, url, _installationId[..8] + "...");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Lifecycle event {EventType} reported successfully", lifecycleEvent.EventType);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Lifecycle event {EventType} request failed: {StatusCode} {Body}",
                    lifecycleEvent.EventType, response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send lifecycle event {EventType} request", lifecycleEvent.EventType);
        }
    }

    private string GetMineOSVersion()
    {
        // Try to get version from build configuration or assembly
        return _configuration["MINEOS_VERSION"] ?? "unknown";
    }

    private class UsageEvent
    {
        [JsonPropertyName("installation_id")]
        public string InstallationId { get; set; } = string.Empty;

        [JsonPropertyName("server_count")]
        public int? ServerCount { get; set; }

        [JsonPropertyName("active_server_count")]
        public int? ActiveServerCount { get; set; }

        [JsonPropertyName("total_user_count")]
        public int? TotalUserCount { get; set; }

        [JsonPropertyName("active_user_count")]
        public int? ActiveUserCount { get; set; }

        [JsonPropertyName("minecraft_usernames")]
        public List<string>? MinecraftUsernames { get; set; }

        [JsonPropertyName("mineos_version")]
        public string MineOSVersion { get; set; } = string.Empty;

        [JsonPropertyName("total_backups_count")]
        public int? TotalBackupsCount { get; set; }

        [JsonPropertyName("total_worlds_count")]
        public int? TotalWorldsCount { get; set; }

        [JsonPropertyName("servers_with_mods_count")]
        public int? ServersWithModsCount { get; set; }

        [JsonPropertyName("servers_with_plugins_count")]
        public int? ServersWithPluginsCount { get; set; }
    }

    private class LifecycleEvent
    {
        [JsonPropertyName("installation_id")]
        public string InstallationId { get; set; } = string.Empty;

        [JsonPropertyName("event_type")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("mineos_version")]
        public string MineOSVersion { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("metadata")]
        public object? Metadata { get; set; }
    }
}
