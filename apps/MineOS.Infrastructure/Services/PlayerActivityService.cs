using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Infrastructure.Services;

public sealed partial class PlayerActivityService : IPlayerActivityService
{
    private readonly AppDbContext _db;
    private readonly HostOptions _hostOptions;
    private readonly ILogger<PlayerActivityService> _logger;

    // Track last processed line per server to avoid reprocessing
    private static readonly ConcurrentDictionary<string, int> LastProcessedLine = new();

    // Regex patterns for Minecraft log parsing
    // Format: [HH:MM:SS] [Server thread/INFO]: PlayerName joined the game
    // Format: [HH:MM:SS] [Server thread/INFO]: PlayerName left the game
    // Format: [HH:MM:SS] [Server thread/INFO]: PlayerName[/IP:PORT] logged in with entity id X at (X, Y, Z)
    [GeneratedRegex(@"\[(\d{2}:\d{2}:\d{2})\] \[Server thread/INFO\]: (\w+)\[/[^\]]+\] logged in")]
    private static partial Regex JoinRegex();

    [GeneratedRegex(@"\[(\d{2}:\d{2}:\d{2})\] \[Server thread/INFO\]: (\w+) joined the game")]
    private static partial Regex JoinedGameRegex();

    [GeneratedRegex(@"\[(\d{2}:\d{2}:\d{2})\] \[Server thread/INFO\]: (\w+) left the game")]
    private static partial Regex LeaveRegex();

    [GeneratedRegex(@"\[(\d{2}:\d{2}:\d{2})\] \[Server thread/INFO\]: (\w+) was (.+)")]
    private static partial Regex DeathRegex();

    [GeneratedRegex(@"\[(\d{2}:\d{2}:\d{2})\] \[Server thread/INFO\]: (\w+) has made the advancement \[(.+)\]")]
    private static partial Regex AdvancementRegex();

    public PlayerActivityService(
        AppDbContext db,
        IOptions<HostOptions> hostOptions,
        ILogger<PlayerActivityService> logger)
    {
        _db = db;
        _hostOptions = hostOptions.Value;
        _logger = logger;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    public async Task ProcessServerLogsAsync(string serverName, CancellationToken cancellationToken = default)
    {
        var logPath = Path.Combine(GetServerPath(serverName), "logs", "latest.log");
        if (!File.Exists(logPath))
        {
            return;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(logPath, cancellationToken);
            var lastLine = LastProcessedLine.GetOrAdd(serverName, 0);

            // Get date from log file modification time for timestamp calculation
            var logDate = File.GetLastWriteTime(logPath).Date;

            // Load user cache for UUID lookups
            var userCache = await LoadUserCacheAsync(serverName, cancellationToken);

            var newEvents = new List<PlayerActivityEvent>();

            for (var i = lastLine; i < lines.Length; i++)
            {
                var line = lines[i];
                var eventResult = ParseLogLine(line, serverName, logDate, userCache);
                if (eventResult != null)
                {
                    newEvents.Add(eventResult);
                }
            }

            LastProcessedLine[serverName] = lines.Length;

            if (newEvents.Count == 0)
            {
                return;
            }

            // Process events and update sessions
            foreach (var evt in newEvents)
            {
                await ProcessActivityEventAsync(evt, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Processed {Count} activity events for server {Server}", newEvents.Count, serverName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing logs for server {Server}", serverName);
        }
    }

    private PlayerActivityEvent? ParseLogLine(string line, string serverName, DateTime logDate, Dictionary<string, string> userCache)
    {
        // Try join patterns
        var joinMatch = JoinRegex().Match(line);
        if (!joinMatch.Success)
        {
            joinMatch = JoinedGameRegex().Match(line);
        }

        if (joinMatch.Success)
        {
            var time = ParseTime(joinMatch.Groups[1].Value, logDate);
            var playerName = joinMatch.Groups[2].Value;
            var uuid = userCache.GetValueOrDefault(playerName, "");

            return new PlayerActivityEvent
            {
                ServerName = serverName,
                PlayerUuid = uuid,
                PlayerName = playerName,
                Timestamp = time,
                EventType = "join"
            };
        }

        // Try leave pattern
        var leaveMatch = LeaveRegex().Match(line);
        if (leaveMatch.Success)
        {
            var time = ParseTime(leaveMatch.Groups[1].Value, logDate);
            var playerName = leaveMatch.Groups[2].Value;
            var uuid = userCache.GetValueOrDefault(playerName, "");

            return new PlayerActivityEvent
            {
                ServerName = serverName,
                PlayerUuid = uuid,
                PlayerName = playerName,
                Timestamp = time,
                EventType = "leave"
            };
        }

        // Try death pattern
        var deathMatch = DeathRegex().Match(line);
        if (deathMatch.Success)
        {
            var time = ParseTime(deathMatch.Groups[1].Value, logDate);
            var playerName = deathMatch.Groups[2].Value;
            var deathCause = deathMatch.Groups[3].Value;
            var uuid = userCache.GetValueOrDefault(playerName, "");

            return new PlayerActivityEvent
            {
                ServerName = serverName,
                PlayerUuid = uuid,
                PlayerName = playerName,
                Timestamp = time,
                EventType = "death",
                EventData = JsonSerializer.Serialize(new { cause = deathCause })
            };
        }

        // Try advancement pattern
        var advMatch = AdvancementRegex().Match(line);
        if (advMatch.Success)
        {
            var time = ParseTime(advMatch.Groups[1].Value, logDate);
            var playerName = advMatch.Groups[2].Value;
            var advancement = advMatch.Groups[3].Value;
            var uuid = userCache.GetValueOrDefault(playerName, "");

            return new PlayerActivityEvent
            {
                ServerName = serverName,
                PlayerUuid = uuid,
                PlayerName = playerName,
                Timestamp = time,
                EventType = "advancement",
                EventData = JsonSerializer.Serialize(new { advancement })
            };
        }

        return null;
    }

    private static DateTimeOffset ParseTime(string timeStr, DateTime logDate)
    {
        if (TimeSpan.TryParse(timeStr, out var time))
        {
            return new DateTimeOffset(logDate.Add(time), TimeZoneInfo.Local.GetUtcOffset(logDate));
        }
        return DateTimeOffset.Now;
    }

    private async Task ProcessActivityEventAsync(PlayerActivityEvent evt, CancellationToken cancellationToken)
    {
        // Add the event
        _db.PlayerActivityEvents.Add(evt);

        // Handle session management for join/leave events
        if (evt.EventType == "join")
        {
            // Create a new session
            var session = new PlayerSession
            {
                ServerName = evt.ServerName,
                PlayerUuid = evt.PlayerUuid,
                PlayerName = evt.PlayerName,
                JoinedAt = evt.Timestamp
            };
            _db.PlayerSessions.Add(session);
        }
        else if (evt.EventType == "leave")
        {
            // Find and close the most recent open session for this player
            var openSession = await _db.PlayerSessions
                .Where(s => s.ServerName == evt.ServerName &&
                           s.PlayerName == evt.PlayerName &&
                           s.LeftAt == null)
                .OrderByDescending(s => s.JoinedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (openSession != null)
            {
                openSession.LeftAt = evt.Timestamp;
                openSession.DurationSeconds = (long)(evt.Timestamp - openSession.JoinedAt).TotalSeconds;
                openSession.LeaveReason = "disconnect";
            }
        }
    }

    private async Task<Dictionary<string, string>> LoadUserCacheAsync(string serverName, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cachePath = Path.Combine(GetServerPath(serverName), "usercache.json");

        if (!File.Exists(cachePath))
        {
            return result;
        }

        try
        {
            var json = await File.ReadAllTextAsync(cachePath, cancellationToken);
            using var doc = JsonDocument.Parse(json);

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.TryGetProperty("name", out var name) &&
                    entry.TryGetProperty("uuid", out var uuid))
                {
                    result[name.GetString() ?? ""] = uuid.GetString() ?? "";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load usercache.json for server {Server}", serverName);
        }

        return result;
    }

    public async Task<IReadOnlyList<PlayerActivityEventDto>> GetRecentActivityAsync(
        string serverName,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var events = await _db.PlayerActivityEvents
            .Where(e => e.ServerName == serverName)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return events.Select(e => new PlayerActivityEventDto(
            e.Id,
            e.ServerName,
            e.PlayerUuid,
            e.PlayerName,
            e.Timestamp,
            e.EventType,
            e.EventData)).ToList();
    }

    public async Task<IReadOnlyList<PlayerActivityEventDto>> GetPlayerActivityAsync(
        string serverName,
        string playerUuid,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var events = await _db.PlayerActivityEvents
            .Where(e => e.ServerName == serverName && e.PlayerUuid == playerUuid)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return events.Select(e => new PlayerActivityEventDto(
            e.Id,
            e.ServerName,
            e.PlayerUuid,
            e.PlayerName,
            e.Timestamp,
            e.EventType,
            e.EventData)).ToList();
    }

    public async Task<IReadOnlyList<PlayerSessionDto>> GetPlayerSessionsAsync(
        string serverName,
        string playerUuid,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _db.PlayerSessions
            .Where(s => s.ServerName == serverName && s.PlayerUuid == playerUuid)
            .OrderByDescending(s => s.JoinedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return sessions.Select(s => new PlayerSessionDto(
            s.Id,
            s.ServerName,
            s.PlayerUuid,
            s.PlayerName,
            s.JoinedAt,
            s.LeftAt,
            s.DurationSeconds,
            s.LeaveReason)).ToList();
    }

    public async Task<IReadOnlyList<PlayerSessionDto>> GetRecentSessionsAsync(
        string serverName,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _db.PlayerSessions
            .Where(s => s.ServerName == serverName)
            .OrderByDescending(s => s.JoinedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return sessions.Select(s => new PlayerSessionDto(
            s.Id,
            s.ServerName,
            s.PlayerUuid,
            s.PlayerName,
            s.JoinedAt,
            s.LeftAt,
            s.DurationSeconds,
            s.LeaveReason)).ToList();
    }

    public async Task<PlayerActivityStatsDto> GetPlayerActivityStatsAsync(
        string serverName,
        string playerUuid,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _db.PlayerSessions
            .Where(s => s.ServerName == serverName && s.PlayerUuid == playerUuid)
            .ToListAsync(cancellationToken);

        var events = await _db.PlayerActivityEvents
            .Where(e => e.ServerName == serverName && e.PlayerUuid == playerUuid)
            .ToListAsync(cancellationToken);

        var playerName = sessions.FirstOrDefault()?.PlayerName ?? events.FirstOrDefault()?.PlayerName ?? "";
        var totalPlayTime = sessions.Where(s => s.DurationSeconds.HasValue).Sum(s => s.DurationSeconds!.Value);
        var avgSession = sessions.Count > 0 ? totalPlayTime / sessions.Count : 0;
        var firstSeen = sessions.Count > 0 ? sessions.Min(s => s.JoinedAt) : (DateTimeOffset?)null;
        var lastSeen = sessions.Count > 0 ? sessions.Max(s => s.LeftAt ?? s.JoinedAt) : (DateTimeOffset?)null;
        var joinCount = events.Count(e => e.EventType == "join");
        var leaveCount = events.Count(e => e.EventType == "leave");
        var deathCount = events.Count(e => e.EventType == "death");

        return new PlayerActivityStatsDto(
            playerUuid,
            playerName,
            sessions.Count,
            totalPlayTime,
            avgSession,
            firstSeen,
            lastSeen,
            joinCount,
            leaveCount,
            deathCount);
    }

    public async Task CloseOpenSessionsAsync(string serverName, string reason = "server_stop", CancellationToken cancellationToken = default)
    {
        var openSessions = await _db.PlayerSessions
            .Where(s => s.ServerName == serverName && s.LeftAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.Now;
        foreach (var session in openSessions)
        {
            session.LeftAt = now;
            session.DurationSeconds = (long)(now - session.JoinedAt).TotalSeconds;
            session.LeaveReason = reason;
        }

        if (openSessions.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Closed {Count} open sessions for server {Server}", openSessions.Count, serverName);
        }
    }
}
