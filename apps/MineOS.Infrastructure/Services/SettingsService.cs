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
            "Discord webhook URL for server event notifications (start, stop, crash).",
            false, "Discord__WebhookUrl",
            "text", "Notifications", "Discord Webhook URL",
            ComingSoon: true),

        [Keys.LogLevel] = new(
            "Minimum log level for the API. Higher levels reduce log volume.",
            false, "Logging__LogLevel__Default",
            "select", "Advanced", "Log Level",
            Options: "[\"Verbose\",\"Debug\",\"Information\",\"Warning\",\"Error\"]"),
    };

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
        if (SettingsMetadata.TryGetValue(key, out var meta) && meta.ConfigPath != null)
        {
            return _configuration[meta.ConfigPath];
        }

        // Try the key directly as a config path
        return _configuration[key];
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
        var dbSettings = await _db.SystemSettings.ToListAsync(cancellationToken);
        var result = new List<SettingInfo>();

        foreach (var (key, meta) in SettingsMetadata)
        {
            var dbSetting = dbSettings.FirstOrDefault(s => s.Key == key);
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
