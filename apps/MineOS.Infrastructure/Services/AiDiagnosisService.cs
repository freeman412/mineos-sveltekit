// apps/MineOS.Infrastructure/Services/AiDiagnosisService.cs
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Services;

public sealed class AiDiagnosisService : IAiDiagnosisService
{
    private const int LogTailLines = 200;
    private const int MaxInputCharacters = 24_000;

    private readonly IServerPathProvider _paths;
    private readonly IRepository<CrashEvent> _crashEvents;
    private readonly IRepository<CrashDiagnosis> _diagnoses;
    private readonly IAiCompletionService _ai;
    private readonly ISettingsService _settings;
    private readonly IPlayerService _players;
    private readonly ILogger<AiDiagnosisService> _logger;

    public AiDiagnosisService(
        IServerPathProvider paths,
        IRepository<CrashEvent> crashEvents,
        IRepository<CrashDiagnosis> diagnoses,
        IAiCompletionService ai,
        ISettingsService settings,
        IPlayerService players,
        ILogger<AiDiagnosisService> logger)
    {
        _paths = paths;
        _crashEvents = crashEvents;
        _diagnoses = diagnoses;
        _ai = ai;
        _settings = settings;
        _players = players;
        _logger = logger;
    }

    public async Task<DiagnosisPreviewDto> PreviewAsync(string serverName, int crashEventId, CancellationToken ct)
    {
        var redacted = await BuildRedactedInputAsync(serverName, crashEventId, ct);
        return new DiagnosisPreviewDto(redacted.Text, redacted.Text.Length, redacted.RulesApplied);
    }

    public Task<CrashDiagnosisDto> DiagnoseAsync(string serverName, int crashEventId, CancellationToken ct) =>
        throw new NotImplementedException("Task 7");

    public Task<CrashDiagnosisDto?> GetAsync(string serverName, int crashEventId, CancellationToken ct) =>
        throw new NotImplementedException("Task 7");

    private async Task<RedactionResult> BuildRedactedInputAsync(string serverName, int crashEventId, CancellationToken ct)
    {
        var crashEvent = await _crashEvents.FirstOrDefaultAsync(
            e => e.Id == crashEventId && e.ServerName == serverName, ct)
            // KeyNotFoundException, not InvalidOperationException: the endpoints map
            // this to 404 and reserve InvalidOperationException for the hourly cap (429).
            ?? throw new KeyNotFoundException($"Crash event {crashEventId} not found for server '{serverName}'.");

        var sections = new List<string>
        {
            $"Server: {serverName}",
            $"Crash type: {crashEvent.CrashType}",
            $"Detected at: {crashEvent.DetectedAt:u}"
        };

        var report = ReadNearestCrashReport(serverName, crashEvent.DetectedAt);
        if (report is not null)
        {
            sections.Add("--- crash report ---");
            sections.Add(report);
        }
        else
        {
            sections.Add("--- no crash report was produced; log tail only ---");
        }

        var logTail = ReadLogTail(serverName);
        if (!string.IsNullOrWhiteSpace(logTail))
        {
            sections.Add("--- latest.log (tail) ---");
            sections.Add(logTail);
        }

        var raw = string.Join("\n", sections);
        if (raw.Length > MaxInputCharacters)
        {
            raw = raw[..MaxInputCharacters] + "\n--- truncated ---";
        }

        return CrashLogRedactor.Redact(raw, await BuildRedactionOptionsAsync(serverName, ct));
    }

    private async Task<RedactionOptions> BuildRedactionOptionsAsync(string serverName, CancellationToken ct)
    {
        var knownPlayers = new List<string>();
        try
        {
            var players = await _players.ListPlayersAsync(serverName, ct);
            knownPlayers.AddRange(players.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        }
        catch (Exception ex)
        {
            // A missing player list must never block a diagnosis; the regex
            // rules still cover UUIDs and addresses.
            _logger.LogDebug("Could not load players for redaction on {Server}: {Message}", serverName, ex.Message);
        }

        return new RedactionOptions(
            RedactPaths: await BoolSettingAsync(SettingsService.Keys.AiRedactPaths, ct),
            RedactPlayerNames: await BoolSettingAsync(SettingsService.Keys.AiRedactPlayerNames, ct),
            KnownPlayers: knownPlayers);
    }

    private async Task<bool> BoolSettingAsync(string key, CancellationToken ct)
    {
        var value = await _settings.GetAsync(key, ct);
        // Default to redacting when unset — the safe direction.
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private string? ReadNearestCrashReport(string serverName, DateTimeOffset detectedAt)
    {
        var directory = _paths.GetCrashReportsPath(serverName);
        if (!Directory.Exists(directory)) return null;

        var nearest = new DirectoryInfo(directory)
            .GetFiles("*.txt")
            .OrderBy(f => Math.Abs((f.LastWriteTimeUtc - detectedAt.UtcDateTime).TotalSeconds))
            .FirstOrDefault();

        return nearest is null ? null : File.ReadAllText(nearest.FullName);
    }

    private string? ReadLogTail(string serverName)
    {
        var path = _paths.GetLogPath(serverName);
        if (!File.Exists(path)) return null;

        var lines = File.ReadLines(path).ToList();
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Count - LogTailLines)));
    }
}
