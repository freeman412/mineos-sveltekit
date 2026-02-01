using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public class TelemetryService : ITelemetryService
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServerService _serverService;
    private readonly IUserService _userService;
    private readonly HttpClient _httpClient;
    private readonly bool _enabled;
    private readonly string _endpoint;
    private readonly string _installationId;

    public TelemetryService(
        ILogger<TelemetryService> logger,
        IConfiguration configuration,
        IServerService serverService,
        IUserService userService,
        HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _serverService = serverService;
        _userService = userService;
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

            foreach (var server in servers)
            {
                try
                {
                    var status = await _serverService.GetServerStatusAsync(server.Name, cancellationToken);
                    if (status.Status == "up")
                    {
                        activeServerCount++;
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
                MineOSVersion = GetMineOSVersion()
            };

            await SendTelemetryAsync(usageEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send usage telemetry");
        }
    }

    private async Task SendTelemetryAsync(UsageEvent usageEvent, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(usageEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

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

    private static string HashUsername(string username)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(username));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
    }
}
