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
    }

    // Metadata for known settings (description, whether it's secret, config fallback path)
    private static readonly Dictionary<string, (string Description, bool IsSecret, string? ConfigPath)> SettingsMetadata = new()
    {
        [Keys.CurseForgeApiKey] = ("CurseForge API key for mod and modpack downloads", true, "CurseForge:ApiKey"),
        [Keys.ShutdownTimeoutSeconds] = ("Seconds to wait for Minecraft servers to stop gracefully.", false, "MINEOS_SHUTDOWN_TIMEOUT")
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
        if (SettingsMetadata.TryGetValue(key, out var metadata) && metadata.ConfigPath != null)
        {
            return _configuration[metadata.ConfigPath];
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

            if (SettingsMetadata.TryGetValue(key, out var metadata))
            {
                setting.Description = metadata.Description;
                setting.IsSecret = metadata.IsSecret;
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

        foreach (var (key, metadata) in SettingsMetadata)
        {
            var dbSetting = dbSettings.FirstOrDefault(s => s.Key == key);
            var dbValue = dbSetting?.Value;
            var configValue = metadata.ConfigPath != null ? _configuration[metadata.ConfigPath] : null;

            string? displayValue = null;
            string source;

            if (!string.IsNullOrWhiteSpace(dbValue))
            {
                displayValue = metadata.IsSecret ? MaskSecret(dbValue) : dbValue;
                source = "database";
            }
            else if (!string.IsNullOrWhiteSpace(configValue))
            {
                displayValue = metadata.IsSecret ? MaskSecret(configValue) : configValue;
                source = "configuration";
            }
            else
            {
                source = "not set";
            }

            result.Add(new SettingInfo(
                Key: key,
                Value: displayValue,
                Description: metadata.Description,
                IsSecret: metadata.IsSecret,
                HasValue: !string.IsNullOrWhiteSpace(dbValue) || !string.IsNullOrWhiteSpace(configValue),
                Source: source
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
