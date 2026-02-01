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
    private readonly HttpClient _httpClient;
    private readonly bool _enabled;
    private readonly string _endpoint;
    private readonly string _installationId;
    private readonly string _version;

    public TelemetryService(
        ILogger<TelemetryService> logger,
        IConfiguration configuration,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);

        _enabled = configuration["MINEOS_TELEMETRY_ENABLED"]?.ToLowerInvariant() != "false";
        _endpoint = configuration["MINEOS_TELEMETRY_ENDPOINT"] ?? "https://mineos.net";
        _installationId = configuration["MINEOS_INSTALLATION_ID"] ?? string.Empty;
        _version = configuration["MINEOS_VERSION"] ?? "unknown";
    }

    public async Task ReportUsageAsync(UsageData data, CancellationToken cancellationToken = default)
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
            var payload = new UsagePayload
            {
                InstallationId = _installationId,
                MineOSVersion = _version,
                ServerCount = data.ServerCount,
                ActiveServerCount = data.ActiveServerCount,
                TotalUserCount = data.TotalUserCount,
                ActiveUserCount = data.TotalUserCount, // All users considered active for now
                TotalBackupsCount = data.TotalBackupsCount,
                TotalWorldsCount = data.TotalWorldsCount,
                ServersWithModsCount = data.ServersWithModsCount,
                ServersWithPluginsCount = data.ServersWithPluginsCount
            };

            await PostJsonAsync($"{_endpoint}/api/telemetry/usage", payload, cancellationToken);
            _logger.LogInformation("Usage telemetry reported successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send usage telemetry");
        }
    }

    public async Task ReportLifecycleEventAsync(string eventType, object? metadata = null, CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrEmpty(_installationId))
            return;

        try
        {
            var payload = new LifecyclePayload
            {
                InstallationId = _installationId,
                EventType = eventType,
                MineOSVersion = _version,
                Timestamp = DateTime.UtcNow,
                Metadata = metadata
            };

            await PostJsonAsync($"{_endpoint}/api/telemetry/event", payload, cancellationToken);
            _logger.LogInformation("Lifecycle event {EventType} reported successfully", eventType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send lifecycle event {EventType}", eventType);
        }
    }

    private async Task PostJsonAsync<T>(string url, T payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Posting telemetry to {Url} for installation {Id}",
            url, _installationId[..8] + "...");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Telemetry request to {Url} failed: {StatusCode} {Body}",
                url, response.StatusCode, body);
        }
    }

    private class UsagePayload
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

    private class LifecyclePayload
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
