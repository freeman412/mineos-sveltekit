namespace MineOS.Application.Interfaces;

public interface ISettingsService
{
    /// <summary>
    /// Get a setting value. Checks database first, then falls back to configuration.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Set a setting value in the database.
    /// </summary>
    Task SetAsync(string key, string? value, CancellationToken cancellationToken);

    /// <summary>
    /// Check if a setting has a value (either in database or config).
    /// </summary>
    Task<bool> HasValueAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Get all configurable settings with their metadata.
    /// </summary>
    Task<IReadOnlyList<SettingInfo>> GetAllAsync(CancellationToken cancellationToken);
}

public record SettingInfo(
    string Key,
    string? Value,
    string? Description,
    bool IsSecret,
    bool HasValue,
    string Source,
    string Type = "text",
    string Group = "General",
    string? DisplayName = null,
    string? Options = null,
    int? Min = null,
    int? Max = null);
