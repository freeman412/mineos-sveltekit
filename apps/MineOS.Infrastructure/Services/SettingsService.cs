using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Infrastructure.Services;

public sealed class SettingsService : ISettingsService
{

    // Well-known setting keys
    public static class Keys
    {
        public const string CurseForgeApiKey = "CurseForge:ApiKey";
        public const string ShutdownTimeoutSeconds = "MineOS:ShutdownTimeoutSeconds";
        public const string TelemetryEnabled = "MineOS:TelemetryEnabled";
        public const string DiscordWebhookUrl = "Discord:WebhookUrl";
        public const string LogLevel = "MineOS:LogLevel";
        public const string TelemetryKey = "MineOS:TelemetryKey";
        public const string AiEnabled = "Ai:Enabled";
        public const string AiBaseUrl = "Ai:BaseUrl";
        public const string AiApiKey = "Ai:ApiKey";
        public const string AiModel = "Ai:Model";
        public const string AiMaxTokens = "Ai:MaxTokens";
        public const string AiTimeoutSeconds = "Ai:TimeoutSeconds";
        public const string AiMaxDiagnosesPerHour = "Ai:MaxDiagnosesPerHour";
        public const string AiRedactPaths = "Ai:RedactPaths";
        public const string AiRedactPlayerNames = "Ai:RedactPlayerNames";
    }

    // Metadata for known settings
    private record SettingMeta(
        string Description,
        bool IsSecret,
        string? ConfigPath,
        string Type,        // "boolean", "number", "text", "secret", "select"
        string Group,       // "General", "Integrations", "Notifications", "Advanced"
        string DisplayName,
        string? Options = null,  // JSON array for select type
        int? Min = null,
        int? Max = null,
        bool ComingSoon = false);

    private static readonly Dictionary<string, SettingMeta> SettingsMetadata = new()
    {
        [Keys.TelemetryEnabled] = new(
            "Send anonymous usage statistics (server count, user count, backups, worlds, mods/plugins) and lifecycle events (startup, shutdown, server creation/deletion, crashes) to help improve MineOS. No personal information, player activity, or server names are collected.",
            false, "MINEOS_TELEMETRY_ENABLED",
            "boolean", "General", "Usage Statistics"),

        [Keys.ShutdownTimeoutSeconds] = new(
            "Seconds to wait for Minecraft servers to stop gracefully before forcing shutdown.",
            false, "MINEOS_SHUTDOWN_TIMEOUT",
            "number", "General", "Shutdown Timeout",
            Min: 0, Max: 900),

        [Keys.CurseForgeApiKey] = new(
            "CurseForge API key for mod and modpack downloads. Get one at console.curseforge.com.",
            true, "CurseForge:ApiKey",
            "secret", "Integrations", "CurseForge API Key"),

        [Keys.DiscordWebhookUrl] = new(
            "Discord webhook URL for server event notifications (start, stop, crash, backups, create/delete).",
            false, "Discord__WebhookUrl",
            "text", "Notifications", "Discord Webhook URL"),

        [Keys.LogLevel] = new(
            "Minimum log level for the API. Higher levels reduce log volume.",
            false, "Logging__LogLevel__Default",
            "select", "Advanced", "Log Level",
            Options: "[\"Verbose\",\"Debug\",\"Information\",\"Warning\",\"Error\"]"),

        [Keys.AiEnabled] = new(
            "Enable AI-assisted crash diagnosis. Requires an endpoint and model below.",
            false, "MINEOS_AI_ENABLED",
            "boolean", "AI", "Enable AI Features"),

        [Keys.AiBaseUrl] = new(
            "Base URL of any endpoint speaking the OpenAI-compatible /chat/completions format. "
            + "Examples: http://localhost:11434/v1 (Ollama), http://localhost:1234/v1 (LM Studio), "
            + "vLLM, llama.cpp, LiteLLM, OpenRouter, https://api.openai.com/v1.",
            false, "MINEOS_AI_BASE_URL",
            "text", "AI", "AI Endpoint URL"),

        [Keys.AiApiKey] = new(
            "API key for the endpoint above. Leave blank for local endpoints that do not require one.",
            true, "MINEOS_AI_API_KEY",
            "secret", "AI", "AI API Key"),

        [Keys.AiModel] = new(
            "Model name to request, exactly as the endpoint expects it (for example llama3.1:8b or gpt-4o-mini).",
            false, "MINEOS_AI_MODEL",
            "text", "AI", "AI Model"),

        [Keys.AiMaxTokens] = new(
            "Maximum tokens in the diagnosis response.",
            false, "MINEOS_AI_MAX_TOKENS",
            "number", "AI", "Max Response Tokens", Min: 100, Max: 4000),

        [Keys.AiTimeoutSeconds] = new(
            "Seconds to wait for the endpoint before giving up.",
            false, "MINEOS_AI_TIMEOUT_SECONDS",
            "number", "AI", "Request Timeout", Min: 5, Max: 300),

        [Keys.AiMaxDiagnosesPerHour] = new(
            "Maximum diagnoses run per hour across all servers. Cached results do not count. 0 disables diagnosis.",
            false, "MINEOS_AI_MAX_DIAGNOSES_PER_HOUR",
            "number", "AI", "Diagnoses Per Hour", Min: 0, Max: 100),

        [Keys.AiRedactPaths] = new(
            "Replace host filesystem paths before sending a log. Mod filenames are always preserved.",
            false, "MINEOS_AI_REDACT_PATHS",
            "boolean", "AI", "Redact File Paths"),

        [Keys.AiRedactPlayerNames] = new(
            "Replace player names and UUIDs before sending a log.",
            false, "MINEOS_AI_REDACT_PLAYER_NAMES",
            "boolean", "AI", "Redact Player Names"),
    };

    public static bool HasMetadata(string key) => SettingsMetadata.ContainsKey(key);

    public static bool IsSecretSetting(string key) =>
        SettingsMetadata.TryGetValue(key, out var meta) && meta.IsSecret;

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<SettingsService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        // First check database
        var setting = await _db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting?.Value != null)
        {
            return setting.Value;
        }

        // Fall back to configuration (appsettings.json / environment variables)
        string? value = null;
        if (SettingsMetadata.TryGetValue(key, out var meta) && meta.ConfigPath != null)
        {
            value = _configuration[meta.ConfigPath];
        }

        // Try the key directly as a config path
        value ??= _configuration[key];

        return value;
    }

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var setting = await _db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            if (SettingsMetadata.TryGetValue(key, out var meta))
            {
                setting.Description = meta.Description;
                setting.IsSecret = meta.IsSecret;
            }

            _db.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Setting {Key} updated", key);
    }

    public async Task<bool> HasValueAsync(string key, CancellationToken cancellationToken)
    {
        var value = await GetAsync(key, cancellationToken);
        return !string.IsNullOrWhiteSpace(value);
    }

    public async Task<IReadOnlyList<SettingInfo>> GetAllAsync(CancellationToken cancellationToken)
    {
        var dbSettings = await _db.SystemSettings.ToDictionaryAsync(s => s.Key, s => s, cancellationToken);
        var result = new List<SettingInfo>();

        foreach (var (key, meta) in SettingsMetadata)
        {
            dbSettings.TryGetValue(key, out var dbSetting);
            var dbValue = dbSetting?.Value;
            var configValue = meta.ConfigPath != null ? _configuration[meta.ConfigPath] : null;

            string? displayValue = null;
            string source;

            if (!string.IsNullOrWhiteSpace(dbValue))
            {
                displayValue = meta.IsSecret ? MaskSecret(dbValue) : dbValue;
                source = "database";
            }
            else if (!string.IsNullOrWhiteSpace(configValue))
            {
                displayValue = meta.IsSecret ? MaskSecret(configValue) : configValue;
                source = "configuration";
            }
            else
            {
                source = "not set";
            }

            result.Add(new SettingInfo(
                Key: key,
                Value: displayValue,
                Description: meta.Description,
                IsSecret: meta.IsSecret,
                HasValue: !string.IsNullOrWhiteSpace(dbValue) || !string.IsNullOrWhiteSpace(configValue),
                Source: source,
                Type: meta.Type,
                Group: meta.Group,
                DisplayName: meta.DisplayName,
                Options: meta.Options,
                Min: meta.Min,
                Max: meta.Max,
                ComingSoon: meta.ComingSoon
            ));
        }

        return result;
    }

    private static string MaskSecret(string value)
    {
        if (value.Length <= 8)
        {
            return new string('*', value.Length);
        }
        return value[..4] + new string('*', value.Length - 8) + value[^4..];
    }
}
