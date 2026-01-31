using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _serverService = serverService;
        _userService = userService;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);

        // Read telemetry configuration
        _enabled = configuration["MINEOS_TELEMETRY_ENABLED"]?.ToLowerInvariant() != "false";
        _endpoint = configuration["MINEOS_TELEMETRY_ENDPOINT"] ?? "https://mineos.net";
        _installationId = configuration["MINEOS_INSTALLATION_ID"] ?? string.Empty;
    }

    public async Task ReportUsageAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrEmpty(_installationId))
        {
            return; // Silently skip if disabled or no installation ID
        }

        try
        {
            // Gather usage statistics
            var servers = await _serverService.ListServersAsync(cancellationToken);
            var users = await _userService.ListUsersAsync(cancellationToken);

            var serverCount = servers.Count;
            var activeServerCount = 0;
            var minecraftUsernames = new HashSet<string>();

            foreach (var server in servers)
            {
                try
                {
                    var status = await _serverService.GetServerStatusAsync(server.Name, cancellationToken);
                    if (status.Status == "up")
                    {
                        activeServerCount++;
                    }

                    // Collect Minecraft usernames from server config (if available)
                    // This would typically come from server.properties or whitelist
                    // For now, we'll leave it empty - implementation can be added later
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
                MinecraftUsernames = minecraftUsernames.Select(HashUsername).ToList(),
                MineOSVersion = GetMineOSVersion()
            };

            await SendTelemetryAsync(usageEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log but don't throw - telemetry failures should not affect application
            _logger.LogDebug(ex, "Failed to send telemetry");
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

            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Telemetry request failed with status: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send telemetry request");
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
