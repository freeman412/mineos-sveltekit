using System.Net;
using System.Net.Http.Headers;
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
    private readonly ISettingsService _settingsService;
    private readonly bool _enabled;
    private readonly string _endpoint;
    private readonly string _installationId;
    private readonly string _version;

    private static readonly SemaphoreSlim KeyLock = new(1, 1);
    private static string? _cachedTelemetryKey;

    public TelemetryService(
        ILogger<TelemetryService> logger,
        IConfiguration configuration,
        HttpClient httpClient,
        ISettingsService settingsService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _settingsService = settingsService;

        _enabled = configuration["MINEOS_TELEMETRY_ENABLED"]?.ToLowerInvariant() != "false";
        _endpoint = configuration["MINEOS_TELEMETRY_ENDPOINT"] ?? "https://mineos.net";
        _installationId = configuration["MINEOS_INSTALLATION_ID"] ?? string.Empty;
        _version = configuration["MINEOS_VERSION"]
                   ?? configuration["MINEOS_IMAGE_TAG"]
                   ?? "unknown";
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
                ActiveUserCount = data.ActiveUserCount,
                UptimeSeconds = data.UptimeSeconds
            };

            await SendWithAuthAsync($"{_endpoint}/api/telemetry/usage", payload, cancellationToken);
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
                Metadata = metadata
            };

            await SendWithAuthAsync($"{_endpoint}/api/telemetry/events", payload, cancellationToken);
            _logger.LogInformation("Lifecycle event {EventType} reported successfully", eventType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send lifecycle event {EventType}", eventType);
        }
    }

    /// <summary>
    /// Sends a JSON payload with Bearer auth. On 401, invalidates the cached key, re-registers, and retries once.
    /// </summary>
    private async Task SendWithAuthAsync<T>(string url, T payload, CancellationToken cancellationToken)
    {
        var key = await EnsureTelemetryKeyAsync(cancellationToken);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var response = await PostWithBearerAsync(url, json, key, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogInformation("Telemetry key rejected (401), re-registering...");
            _cachedTelemetryKey = null;
            key = await RegisterAndGetKeyAsync(cancellationToken);
            response = await PostWithBearerAsync(url, json, key, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> PostWithBearerAsync(
        string url, string json, string? bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        _logger.LogDebug("Posting telemetry to {Url} for installation {Id}",
            url, _installationId[..8] + "...");

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Returns a telemetry key, checking the in-memory cache first, then the DB, then registering a new one.
    /// </summary>
    private async Task<string?> EnsureTelemetryKeyAsync(CancellationToken cancellationToken)
    {
        if (_cachedTelemetryKey != null)
            return _cachedTelemetryKey;

        await KeyLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedTelemetryKey != null)
                return _cachedTelemetryKey;

            var dbKey = await _settingsService.GetAsync(SettingsService.Keys.TelemetryKey, cancellationToken);
            if (!string.IsNullOrEmpty(dbKey))
            {
                _cachedTelemetryKey = dbKey;
                return dbKey;
            }

            return await RegisterAndGetKeyAsync(cancellationToken);
        }
        finally
        {
            KeyLock.Release();
        }
    }

    /// <summary>
    /// Calls POST /api/telemetry/install to register and obtain a telemetry_key.
    /// Stores the key in the DB and caches it in memory.
    /// </summary>
    private async Task<string?> RegisterAndGetKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var registerPayload = new { installation_id = _installationId };
            var json = JsonSerializer.Serialize(registerPayload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Registering for telemetry key at {Endpoint}...", _endpoint);

            var response = await _httpClient.PostAsync(
                $"{_endpoint}/api/telemetry/install", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var installResponse = JsonSerializer.Deserialize<InstallResponse>(responseJson, JsonOptions);

            var key = installResponse?.TelemetryKey;
            if (string.IsNullOrEmpty(key))
            {
                _logger.LogWarning("Telemetry install response did not contain a telemetry_key");
                return null;
            }

            await _settingsService.SetAsync(SettingsService.Keys.TelemetryKey, key, cancellationToken);
            _cachedTelemetryKey = key;

            _logger.LogInformation("Telemetry key obtained and stored successfully");
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register for telemetry key");
            return null;
        }
    }

    private class InstallResponse
    {
        [JsonPropertyName("telemetry_key")]
        public string? TelemetryKey { get; set; }
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

        [JsonPropertyName("uptime_seconds")]
        public long UptimeSeconds { get; set; }

        [JsonPropertyName("mineos_version")]
        public string MineOSVersion { get; set; } = string.Empty;
    }

    private class LifecyclePayload
    {
        [JsonPropertyName("installation_id")]
        public string InstallationId { get; set; } = string.Empty;

        [JsonPropertyName("event_type")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("mineos_version")]
        public string MineOSVersion { get; set; } = string.Empty;

        [JsonPropertyName("metadata")]
        public object? Metadata { get; set; }
    }
}
