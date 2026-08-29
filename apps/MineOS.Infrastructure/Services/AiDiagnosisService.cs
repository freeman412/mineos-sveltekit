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

    public sealed record DiagnosisPayload(
        string? Summary,
        string? LikelyCause,
        IReadOnlyList<string> SuggestedActions,
        string Classification,
        string Confidence);

    private static readonly string[] ValidClassifications =
        { "mineos-bug", "mod-or-modpack", "environment", "unknown" };

    private const string SystemPrompt =
        "You are a Minecraft server administrator's assistant. You are given a redacted crash report "
        + "and log tail. Reply with ONLY a JSON object and no prose, using these keys: "
        + "summary (one sentence), likelyCause (one or two sentences), suggestedActions (array of short strings), "
        + "classification (one of: mineos-bug, mod-or-modpack, environment, unknown), "
        + "confidence (one of: low, medium, high). "
        + "Never invent mod names, versions or file names that do not appear in the input. "
        + "If the input is insufficient, say so in summary and use classification \"unknown\".";

    public static DiagnosisPayload ParseResponse(string content)
    {
        var text = content.Trim();

        // Models routinely wrap JSON in fences despite being told not to.
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
            {
                text = text[(firstNewline + 1)..lastFence].Trim();
            }
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var root = doc.RootElement;

            var actions = new List<string>();
            if (root.TryGetProperty("suggestedActions", out var list) &&
                list.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                actions.AddRange(list.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))!);
            }

            var classification = root.TryGetProperty("classification", out var c) ? c.GetString() : null;
            if (classification is null || !ValidClassifications.Contains(classification))
            {
                classification = "unknown";
            }

            var confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetString() : null;
            if (confidence is not ("low" or "medium" or "high")) confidence = "low";

            return new DiagnosisPayload(
                root.TryGetProperty("summary", out var s) ? s.GetString() : null,
                root.TryGetProperty("likelyCause", out var l) ? l.GetString() : null,
                actions,
                classification,
                confidence);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new FormatException("The model did not return JSON.", ex);
        }
    }

    public async Task<CrashDiagnosisDto?> GetAsync(string serverName, int crashEventId, CancellationToken ct)
    {
        var existing = await _diagnoses.FirstOrDefaultAsync(
            d => d.ServerName == serverName && d.CrashEventId == crashEventId, ct);
        return existing is null ? null : ToDto(existing);
    }

    public async Task<CrashDiagnosisDto> DiagnoseAsync(string serverName, int crashEventId, CancellationToken ct)
    {
        var redacted = await BuildRedactedInputAsync(serverName, crashEventId, ct);
        var model = await _settings.GetAsync(SettingsService.Keys.AiModel, ct) ?? "unknown";
        var hash = Hash(redacted.Text + model);

        var cached = await _diagnoses.FirstOrDefaultAsync(
            d => d.ServerName == serverName && d.SourceHash == hash, ct);
        if (cached is not null) return ToDto(cached);

        await EnsureUnderHourlyCapAsync(ct);

        var result = await _ai.CompleteAsync(new AiCompletionRequest(SystemPrompt, redacted.Text), ct);

        var row = new CrashDiagnosis
        {
            CrashEventId = crashEventId,
            ServerName = serverName,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceHash = hash,
            Model = result.Model ?? model,
            RedactedInput = redacted.Text,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens
        };

        if (!result.Success)
        {
            row.Status = "failed";
            row.Error = result.ErrorMessage;
        }
        else
        {
            try
            {
                var payload = ParseResponse(result.Content!);
                row.Status = "complete";
                row.Summary = payload.Summary;
                row.LikelyCause = payload.LikelyCause;
                row.SuggestedActions = System.Text.Json.JsonSerializer.Serialize(payload.SuggestedActions);
                row.Classification = payload.Classification;
                row.Confidence = payload.Confidence;
            }
            catch (FormatException)
            {
                // Never surface unparseable output as if it were a diagnosis.
                row.Status = "failed";
                row.Error = "The model did not return a readable diagnosis.";
            }
        }

        await _diagnoses.AddAsync(row, ct);
        return ToDto(row);
    }

    private async Task EnsureUnderHourlyCapAsync(CancellationToken ct)
    {
        var configured = await _settings.GetAsync(SettingsService.Keys.AiMaxDiagnosesPerHour, ct);
        var cap = int.TryParse(configured, out var parsed) ? parsed : 20;
        if (cap == 0)
        {
            throw new InvalidOperationException("AI diagnosis is disabled (Diagnoses Per Hour is set to 0).");
        }

        // Counted in the database, so the cap survives an API restart.
        var since = DateTimeOffset.UtcNow.AddHours(-1);
        var recent = await _diagnoses.ToListAsync(d => d.CreatedAt >= since, ct);
        if (recent.Count >= cap)
        {
            throw new InvalidOperationException(
                $"The hourly diagnosis limit ({cap}) has been reached. Adjust 'Diagnoses Per Hour' in Settings.");
        }
    }

    private static string Hash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static CrashDiagnosisDto ToDto(CrashDiagnosis row)
    {
        var actions = string.IsNullOrWhiteSpace(row.SuggestedActions)
            ? new List<string>()
            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(row.SuggestedActions) ?? new List<string>();

        return new CrashDiagnosisDto(
            row.Id, row.CrashEventId, row.ServerName, row.CreatedAt, row.Model,
            row.Summary, row.LikelyCause, actions, row.Classification, row.Confidence,
            row.Status, row.Error);
    }

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

        try
        {
            var nearest = new DirectoryInfo(directory)
                .GetFiles("*.txt")
                .OrderBy(f => Math.Abs((f.LastWriteTimeUtc - detectedAt.UtcDateTime).TotalSeconds))
                .FirstOrDefault();

            return nearest is null ? null : File.ReadAllText(nearest.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The crash report can be deleted, rotated or locked between the directory
            // listing and the read — especially likely during a crash. Degrade gracefully.
            _logger.LogDebug("Could not read crash report for {Server}: {Message}", serverName, ex.Message);
            return null;
        }
    }

    private string? ReadLogTail(string serverName)
    {
        var path = _paths.GetLogPath(serverName);
        if (!File.Exists(path)) return null;

        try
        {
            var lines = File.ReadLines(path).ToList();
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Count - LogTailLines)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // latest.log can be rotated or locked between the existence check and the
            // read — especially likely during a crash. Degrade gracefully.
            _logger.LogDebug("Could not read log tail for {Server}: {Message}", serverName, ex.Message);
            return null;
        }
    }
}
