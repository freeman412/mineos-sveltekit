using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class PlayerService : IPlayerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HostOptions _hostOptions;
    private readonly ILogger<PlayerService> _logger;
    private readonly IProcessManager _processManager;

    public PlayerService(
        IOptions<HostOptions> hostOptions,
        ILogger<PlayerService> logger,
        IProcessManager processManager)
    {
        _hostOptions = hostOptions.Value;
        _logger = logger;
        _processManager = processManager;
    }

    public async Task<IReadOnlyList<PlayerSummaryDto>> ListPlayersAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var userCache = await LoadJsonListAsync<UserCacheEntry>(GetUserCachePath(serverName), cancellationToken);
        var whitelist = await LoadJsonListAsync<WhitelistEntry>(GetWhitelistPath(serverName), cancellationToken);
        var ops = await LoadJsonListAsync<OpEntry>(GetOpsPath(serverName), cancellationToken);
        var bans = await LoadJsonListAsync<BanEntry>(GetBannedPlayersPath(serverName), cancellationToken);

        var players = new Dictionary<string, PlayerAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in userCache)
        {
            if (string.IsNullOrWhiteSpace(entry.Uuid))
            {
                continue;
            }

            EnsurePlayer(players, entry.Uuid, entry.Name);
        }

        foreach (var entry in whitelist)
        {
            if (string.IsNullOrWhiteSpace(entry.Uuid))
            {
                continue;
            }

            var player = EnsurePlayer(players, entry.Uuid, entry.Name);
            player.Whitelisted = true;
        }

        foreach (var entry in ops)
        {
            if (string.IsNullOrWhiteSpace(entry.Uuid))
            {
                continue;
            }

            var player = EnsurePlayer(players, entry.Uuid, entry.Name);
            player.IsOp = true;
            player.OpLevel = entry.Level;
            player.OpBypassPlayerLimit = entry.BypassesPlayerLimit;
        }

        foreach (var entry in bans)
        {
            if (string.IsNullOrWhiteSpace(entry.Uuid))
            {
                continue;
            }

            var player = EnsurePlayer(players, entry.Uuid, entry.Name);
            player.BanReason = entry.Reason;
            player.BanExpiresAt = ParseBanExpires(entry.Expires);
            player.Banned = IsBanActive(player.BanExpiresAt);
        }

        var worldPath = FindWorldPath(serverPath);
        var statsDir = worldPath == null ? null : Path.Combine(worldPath, "stats");
        var playerDataDir = worldPath == null ? null : Path.Combine(worldPath, "playerdata");

        if (statsDir != null && Directory.Exists(statsDir))
        {
            foreach (var statsFile in Directory.EnumerateFiles(statsDir, "*.json"))
            {
                var uuid = Path.GetFileNameWithoutExtension(statsFile);
                if (string.IsNullOrWhiteSpace(uuid))
                {
                    continue;
                }

                EnsurePlayer(players, uuid, null);
            }
        }

        if (playerDataDir != null && Directory.Exists(playerDataDir))
        {
            foreach (var dataFile in Directory.EnumerateFiles(playerDataDir, "*.dat"))
            {
                var uuid = Path.GetFileNameWithoutExtension(dataFile);
                if (string.IsNullOrWhiteSpace(uuid))
                {
                    continue;
                }

                EnsurePlayer(players, uuid, null);
            }
        }

        foreach (var player in players.Values)
        {
            if (statsDir != null)
            {
                var statsPath = Path.Combine(statsDir, $"{player.Uuid}.json");
                if (File.Exists(statsPath))
                {
                    player.PlayTimeSeconds = await TryGetPlayTimeSecondsAsync(statsPath, cancellationToken);
                    player.LastSeen = GetMostRecent(player.LastSeen, File.GetLastWriteTimeUtc(statsPath));
                }
            }

            if (playerDataDir != null)
            {
                var playerDataPath = Path.Combine(playerDataDir, $"{player.Uuid}.dat");
                if (File.Exists(playerDataPath))
                {
                    player.LastSeen = GetMostRecent(player.LastSeen, File.GetLastWriteTimeUtc(playerDataPath));
                }
            }
        }

        return players.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PlayerSummaryDto(
                p.Uuid,
                p.Name,
                p.Whitelisted,
                p.IsOp,
                p.OpLevel,
                p.OpBypassPlayerLimit,
                p.Banned,
                p.BanReason,
                p.BanExpiresAt,
                p.LastSeen,
                p.PlayTimeSeconds))
            .ToList();
    }

    public async Task<PlayerStatsDto> GetPlayerStatsAsync(
        string serverName,
        string uuid,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var worldPath = FindWorldPath(serverPath);
        if (worldPath == null)
        {
            throw new FileNotFoundException("No world folder found for server.");
        }

        var statsPath = Path.Combine(worldPath, "stats", $"{uuid}.json");
        if (!File.Exists(statsPath))
        {
            throw new FileNotFoundException($"Stats file not found for player {uuid}.");
        }

        var rawJson = await File.ReadAllTextAsync(statsPath, cancellationToken);
        var lastModified = File.GetLastWriteTimeUtc(statsPath);
        return new PlayerStatsDto(uuid, rawJson, lastModified);
    }

    public async Task WhitelistPlayerAsync(
        string serverName,
        string uuid,
        string? name,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var resolvedName = ResolvePlayerName(serverName, uuid, name);
        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            throw new InvalidOperationException("Player name is required to whitelist.");
        }

        var entries = await LoadJsonListAsync<WhitelistEntry>(GetWhitelistPath(serverName), cancellationToken);
        var existing = entries.FirstOrDefault(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            entries.Remove(existing);
        }

        entries.Add(new WhitelistEntry(uuid, resolvedName));
        await SaveJsonListAsync(GetWhitelistPath(serverName), entries, cancellationToken);
    }

    public async Task RemoveWhitelistAsync(string serverName, string uuid, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var entries = await LoadJsonListAsync<WhitelistEntry>(GetWhitelistPath(serverName), cancellationToken);
        entries.RemoveAll(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        await SaveJsonListAsync(GetWhitelistPath(serverName), entries, cancellationToken);
    }

    public async Task OpPlayerAsync(
        string serverName,
        string uuid,
        string? name,
        int level,
        bool bypassesPlayerLimit,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var resolvedName = ResolvePlayerName(serverName, uuid, name);
        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            throw new InvalidOperationException("Player name is required to OP.");
        }

        var entries = await LoadJsonListAsync<OpEntry>(GetOpsPath(serverName), cancellationToken);
        var existing = entries.FirstOrDefault(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            entries.Remove(existing);
        }

        entries.Add(new OpEntry(uuid, resolvedName, level, bypassesPlayerLimit));
        await SaveJsonListAsync(GetOpsPath(serverName), entries, cancellationToken);

        // Send command to running server if applicable
        var isRunning = await _processManager.IsServerRunningAsync(serverName, cancellationToken);
        if (isRunning)
        {
            await _processManager.SendCommandAsync(
                serverName,
                $"op {resolvedName}",
                _hostOptions.RunAsUid,
                _hostOptions.RunAsGid,
                cancellationToken);
            _logger.LogInformation("Sent OP command to running server {ServerName} for player {PlayerName}", serverName, resolvedName);
        }
    }

    public async Task BanPlayerAsync(
        string serverName,
        string uuid,
        string? name,
        string? reason,
        string? bannedBy,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var resolvedName = ResolvePlayerName(serverName, uuid, name);
        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            throw new InvalidOperationException("Player name is required to ban.");
        }

        var entries = await LoadJsonListAsync<BanEntry>(GetBannedPlayersPath(serverName), cancellationToken);
        var existing = entries.FirstOrDefault(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            entries.Remove(existing);
        }

        var created = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var expires = expiresAt.HasValue
            ? expiresAt.Value.ToString("yyyy-MM-dd HH:mm:ss zzz")
            : "forever";
        var finalReason = string.IsNullOrWhiteSpace(reason) ? "Banned by MineOS" : reason.Trim();
        var source = string.IsNullOrWhiteSpace(bannedBy) ? "MineOS" : bannedBy.Trim();

        entries.Add(new BanEntry(uuid, resolvedName, created, source, expires, finalReason));
        await SaveJsonListAsync(GetBannedPlayersPath(serverName), entries, cancellationToken);

        // Send command to running server if applicable
        var isRunning = await _processManager.IsServerRunningAsync(serverName, cancellationToken);
        if (isRunning)
        {
            var banCommand = string.IsNullOrWhiteSpace(reason)
                ? $"ban {resolvedName}"
                : $"ban {resolvedName} {finalReason}";

            await _processManager.SendCommandAsync(
                serverName,
                banCommand,
                _hostOptions.RunAsUid,
                _hostOptions.RunAsGid,
                cancellationToken);
            _logger.LogInformation("Sent BAN command to running server {ServerName} for player {PlayerName}", serverName, resolvedName);
        }
    }

    public async Task UnbanPlayerAsync(string serverName, string uuid, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var entries = await LoadJsonListAsync<BanEntry>(GetBannedPlayersPath(serverName), cancellationToken);
        var existing = entries.FirstOrDefault(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        var playerName = existing?.Name;

        entries.RemoveAll(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        await SaveJsonListAsync(GetBannedPlayersPath(serverName), entries, cancellationToken);

        // Send command to running server if applicable
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            var isRunning = await _processManager.IsServerRunningAsync(serverName, cancellationToken);
            if (isRunning)
            {
                await _processManager.SendCommandAsync(
                    serverName,
                    $"pardon {playerName}",
                    _hostOptions.RunAsUid,
                    _hostOptions.RunAsGid,
                    cancellationToken);
                _logger.LogInformation("Sent PARDON command to running server {ServerName} for player {PlayerName}", serverName, playerName);
            }
        }
    }

    public async Task DeopPlayerAsync(string serverName, string uuid, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var entries = await LoadJsonListAsync<OpEntry>(GetOpsPath(serverName), cancellationToken);
        var existing = entries.FirstOrDefault(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        var playerName = existing?.Name;

        entries.RemoveAll(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        await SaveJsonListAsync(GetOpsPath(serverName), entries, cancellationToken);

        // Send command to running server if applicable
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            var isRunning = await _processManager.IsServerRunningAsync(serverName, cancellationToken);
            if (isRunning)
            {
                await _processManager.SendCommandAsync(
                    serverName,
                    $"deop {playerName}",
                    _hostOptions.RunAsUid,
                    _hostOptions.RunAsGid,
                    cancellationToken);
                _logger.LogInformation("Sent DEOP command to running server {ServerName} for player {PlayerName}", serverName, playerName);
            }
        }
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetWhitelistPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "whitelist.json");

    private string GetOpsPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "ops.json");

    private string GetBannedPlayersPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "banned-players.json");

    private string GetUserCachePath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "usercache.json");

    private string ResolvePlayerName(string serverName, string uuid, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        var userCachePath = GetUserCachePath(serverName);
        if (!File.Exists(userCachePath))
        {
            return string.Empty;
        }

        try
        {
            var json = File.ReadAllText(userCachePath);
            var entries = JsonSerializer.Deserialize<List<UserCacheEntry>>(json, JsonOptions) ?? new();
            var match = entries.FirstOrDefault(e => string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
            return match?.Name ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve player name from usercache for {Uuid}", uuid);
            return string.Empty;
        }
    }

    private static string? FindWorldPath(string serverPath)
    {
        var preferred = Path.Combine(serverPath, "world");
        if (Directory.Exists(preferred) && IsWorldFolder(preferred))
        {
            return preferred;
        }

        foreach (var dir in Directory.EnumerateDirectories(serverPath))
        {
            if (IsWorldFolder(dir))
            {
                return dir;
            }
        }

        return null;
    }

    private static bool IsWorldFolder(string path)
    {
        return File.Exists(Path.Combine(path, "level.dat")) ||
               File.Exists(Path.Combine(path, "session.lock"));
    }

    private static PlayerAggregate EnsurePlayer(
        Dictionary<string, PlayerAggregate> players,
        string uuid,
        string? name)
    {
        if (!players.TryGetValue(uuid, out var player))
        {
            player = new PlayerAggregate
            {
                Uuid = uuid,
                Name = string.IsNullOrWhiteSpace(name) ? "Unknown" : name
            };
            players[uuid] = player;
            return player;
        }

        if (!string.IsNullOrWhiteSpace(name) && player.Name == "Unknown")
        {
            player.Name = name;
        }

        return player;
    }

    private async Task<List<T>> LoadJsonListAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new List<T>();
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<T>();
        }

        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
    }

    private async Task SaveJsonListAsync<T>(string path, List<T> entries, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(
            path,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);
    }

    private static DateTimeOffset? ParseBanExpires(string? expires)
    {
        if (string.IsNullOrWhiteSpace(expires) ||
            expires.Equals("forever", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return DateTimeOffset.TryParse(expires, out var parsed) ? parsed : null;
    }

    private static bool IsBanActive(DateTimeOffset? expiresAt)
    {
        if (!expiresAt.HasValue)
        {
            return true;
        }

        return expiresAt.Value > DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset? GetMostRecent(DateTimeOffset? current, DateTime timestamp)
    {
        var candidate = new DateTimeOffset(timestamp);
        if (!current.HasValue || candidate > current.Value)
        {
            return candidate;
        }

        return current;
    }

    private static async Task<long?> TryGetPlayTimeSecondsAsync(string statsPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(statsPath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("stats", out var statsElement))
            {
                return null;
            }

            if (!statsElement.TryGetProperty("minecraft:custom", out var custom))
            {
                return null;
            }

            if (custom.TryGetProperty("minecraft:play_time", out var playTime))
            {
                return playTime.GetInt64() / 20;
            }

            if (custom.TryGetProperty("minecraft:play_one_minute", out var legacyPlayTime))
            {
                return legacyPlayTime.GetInt64() / 20;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class PlayerAggregate
    {
        public required string Uuid { get; init; }
        public required string Name { get; set; }
        public bool Whitelisted { get; set; }
        public bool IsOp { get; set; }
        public int? OpLevel { get; set; }
        public bool? OpBypassPlayerLimit { get; set; }
        public bool Banned { get; set; }
        public string? BanReason { get; set; }
        public DateTimeOffset? BanExpiresAt { get; set; }
        public DateTimeOffset? LastSeen { get; set; }
        public long? PlayTimeSeconds { get; set; }
    }

    private sealed record WhitelistEntry(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("name")] string Name);

    private sealed record OpEntry(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("level")] int Level,
        [property: JsonPropertyName("bypassesPlayerLimit")] bool BypassesPlayerLimit);

    private sealed record BanEntry(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("created")] string Created,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("expires")] string Expires,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed record UserCacheEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("expiresOn")] string ExpiresOn);
}
