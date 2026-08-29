// apps/MineOS.Infrastructure/Services/AiDiagnosisService.cs
using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Services;

public sealed class AiDiagnosisService : IAiDiagnosisService
{
    private const int MaxInputCharacters = 64_000;

    // The crash report is capped as its own section rather than by trimming the assembled text
    // from the end. Head-truncating a crash report is safe: the exception and its stack sit at
    // the top, the mod list and system details at the bottom. Head-truncating the distilled log
    // is not — its most valuable content, the verbatim pre-crash tail, is at the end, and it is
    // exactly what the distiller reserves budget for.
    private const int MaxCrashReportCharacters = 24_000;

    // A crash report further than this from the crash belongs to a different crash.
    private static readonly TimeSpan CrashReportWindow = TimeSpan.FromMinutes(10);

    private const string LatestLogHeading = "--- latest.log (whole-session distillation) ---";

    // The budgets have to reconcile with MaxInputCharacters, or the final backstop trims the
    // one thing the distiller reserves budget to protect — the verbatim pre-crash tail, which
    // sits at the very end of the assembled text. Worst case:
    //   crash report        24,000
    //   truncation marker      ~70
    //   distilled log       39,000
    //   metadata + headings   ~400
    //   joining newlines       ~10
    //   ------------------------------
    //                       ~63,480  <  64,000
    private const int MaxDistilledLogCharacters = 39_000;

    private readonly IServerPathProvider _paths;
    private readonly IRepository<CrashEvent> _crashEvents;
    private readonly IRepository<CrashDiagnosis> _diagnoses;
    private readonly IAiCompletionService _ai;
    private readonly ISettingsService _settings;
    private readonly IPlayerService _players;
    private readonly IServerService _servers;
    private readonly ILogger<AiDiagnosisService> _logger;

    public AiDiagnosisService(
        IServerPathProvider paths,
        IRepository<CrashEvent> crashEvents,
        IRepository<CrashDiagnosis> diagnoses,
        IAiCompletionService ai,
        ISettingsService settings,
        IPlayerService players,
        IServerService servers,
        ILogger<AiDiagnosisService> logger)
    {
        _paths = paths;
        _crashEvents = crashEvents;
        _diagnoses = diagnoses;
        _ai = ai;
        _settings = settings;
        _players = players;
        _servers = servers;
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
        + "and a distilled summary of the whole session log, in which repeated errors are collapsed "
        + "into counts and omissions are marked. Reply with ONLY a JSON object and no prose, "
        + "using these keys: "
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
        if (cached is not null)
        {
            // A failure is not an answer. Caching one against a now-stable input — a stopped
            // server's crash, whose text never changes again — would make the crash permanently
            // undiagnosable the moment the endpoint had a bad five minutes. Treat it as a miss
            // and drop the stale row, so the unique index does not then reject the retry.
            if (!string.Equals(cached.Status, "failed", StringComparison.Ordinal)) return ToDto(cached);
            await _diagnoses.RemoveAsync(cached, ct);
        }

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

        try
        {
            await _diagnoses.AddAsync(row, ct);
        }
        catch (DbUpdateException)
        {
            // Two POSTs for the same crash — two browser tabs is enough — both miss the cache
            // and both insert; the unique index on (ServerName, SourceHash) rejects the loser.
            // The winner's row is the same diagnosis, so return it rather than a 500.
            var winner = await _diagnoses.FirstOrDefaultAsync(
                d => d.ServerName == serverName && d.SourceHash == hash, ct);
            if (winner is null) throw;
            return ToDto(winner);
        }

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

        // The loader and the game version are the two most useful facts about a modded crash:
        // "NoSuchMethodError in a Forge 1.20.1 pack" is a different problem from the same
        // exception on Fabric. A missing value is omitted rather than guessed — an invented
        // "unknown" line is worse than silence, because the model will reason from it.
        var loader = await DetectLoaderAsync(serverName, ct);
        if (!string.IsNullOrWhiteSpace(loader?.Loader)) sections.Add($"Server type: {loader!.Loader}");
        if (!string.IsNullOrWhiteSpace(loader?.MinecraftVersion)) sections.Add($"Minecraft version: {loader!.MinecraftVersion}");
        if (!string.IsNullOrWhiteSpace(loader?.Version)) sections.Add($"Loader version: {loader!.Version}");

        var report = ReadNearestCrashReport(serverName, crashEvent.DetectedAt);
        if (report is not null)
        {
            sections.Add("--- crash report ---");
            if (report.Length > MaxCrashReportCharacters)
            {
                var droppedChars = report.Length - MaxCrashReportCharacters;
                sections.Add(report[..MaxCrashReportCharacters]);
                sections.Add($"--- crash report truncated: {droppedChars.ToString("N0", CultureInfo.InvariantCulture)} further characters omitted ---");
            }
            else
            {
                sections.Add(report);
            }
        }
        else
        {
            sections.Add("--- no crash report was produced; distilled log only ---");
        }

        var log = ReadDistilledLog(serverName, crashEvent.DetectedAt);
        if (!string.IsNullOrWhiteSpace(log.Text))
        {
            sections.Add(log.Heading);
            sections.Add(log.Text!);
        }

        var raw = string.Join("\n", sections);
        // Final backstop only: every section is capped above, so this should rarely bind.
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

            if (nearest is null) return null;

            // A ProcessDeath or OutOfMemory crash produces no crash report at all, and "nearest"
            // with no window happily attaches a real stack trace from months ago under a
            // "--- crash report ---" heading. The prompt's "never invent" instruction cannot save
            // the model from that: the content is genuine, it is just about a different crash.
            var distance = Math.Abs((nearest.LastWriteTimeUtc - detectedAt.UtcDateTime).TotalSeconds);
            if (distance > CrashReportWindow.TotalSeconds)
            {
                _logger.LogDebug(
                    "Nearest crash report for {Server} is {Seconds:N0}s from the crash; ignoring it.",
                    serverName, distance);
                return null;
            }

            return File.ReadAllText(nearest.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The crash report can be deleted, rotated or locked between the directory
            // listing and the read — especially likely during a crash. Degrade gracefully.
            _logger.LogDebug("Could not read crash report for {Server}: {Message}", serverName, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The distilled whole-session log for the session that actually crashed, with the heading
    /// that honestly describes what was found.
    /// </summary>
    /// <remarks>
    /// log4j archives latest.log to logs/YYYY-MM-DD-N.log.gz and starts a fresh one on every
    /// server start — and watchdog auto-restart is this product's normal path. So for the most
    /// common crash, latest.log by the time anyone clicks Diagnose is the POST-restart session:
    /// the wrong evidence, presented under a heading that claims it is the crash's own log. It
    /// also guts the cache — on a running server the text changes between clicks, so SourceHash
    /// never matches and every POST is a fresh billed call.
    /// </remarks>
    private (string Heading, string? Text) ReadDistilledLog(string serverName, DateTimeOffset detectedAt)
    {
        var path = _paths.GetLogPath(serverName);
        if (!File.Exists(path)) return (LatestLogHeading, null);

        try
        {
            if (LooksLikeALaterSession(path, detectedAt))
            {
                var archive = FindArchivedSessionLog(path, detectedAt);
                if (archive is not null)
                {
                    return ($"--- {Path.GetFileName(archive)} (whole-session distillation; the crash's own session log) ---",
                        Distill(archive));
                }

                // Still worth sending — an unrelated session shows the modpack and the shape of
                // the server — but the model must not be allowed to read it as the crash's log.
                return (
                    "--- latest.log (whole-session distillation; the crash's own session log was not found) ---",
                    Distill(path));
            }

            return (LatestLogHeading, Distill(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log can be rotated, compressed or locked between the existence check and the
            // read — especially likely during a crash. Degrade gracefully.
            _logger.LogDebug("Could not read the distilled log for {Server}: {Message}", serverName, ex.Message);
            return (LatestLogHeading, null);
        }
    }

    private static string Distill(string path) =>
        CrashLogDistiller.Distill(
            ReadLines(path),
            new LogDistillerOptions { MaxOutputCharacters = MaxDistilledLogCharacters }).Text;

    /// <summary>
    /// Lines from a plain or gzipped log, streamed. Never materialises the file: the distiller's
    /// bounded-memory guarantee over an unbounded log has to survive decompression too.
    /// </summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        if (!path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            // File.ReadLines is lazy and the distiller enumerates it exactly once, so a
            // multi-gigabyte latest.log is streamed rather than loaded.
            return File.ReadLines(path);
        }

        return ReadGzipLines(path);
    }

    private static IEnumerable<string> ReadGzipLines(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    /// <summary>
    /// True when latest.log looks like a session that began after the crash — the signature of
    /// an auto-restart having rotated the crash's own log away.
    /// </summary>
    private static bool LooksLikeALaterSession(string path, DateTimeOffset detectedAt)
    {
        var info = new FileInfo(path);
        var crash = detectedAt.UtcDateTime;
        var lastWrite = info.LastWriteTimeUtc;

        // Not every file system reports a creation time; where it is missing or nonsensical
        // .NET hands back an epoch sentinel or the write time, so fall back to the write time
        // rather than reading "no birth time" as "created long ago".
        var created = info.CreationTimeUtc;
        var firstWrite = created > DateTime.UnixEpoch && created <= lastWrite ? created : lastWrite;

        return lastWrite >= crash && firstWrite > crash;
    }

    /// <summary>
    /// The archived session log whose last write is the closest one at or after the crash —
    /// i.e. the log that was sealed by the restart that followed it.
    /// </summary>
    private static string? FindArchivedSessionLog(string latestLogPath, DateTimeOffset detectedAt)
    {
        var directory = Path.GetDirectoryName(latestLogPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

        var latestName = Path.GetFileName(latestLogPath);
        var crash = detectedAt.UtcDateTime;

        return new DirectoryInfo(directory)
            .EnumerateFiles()
            .Where(f => !string.Equals(f.Name, latestName, StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                        || f.Name.EndsWith(".log.gz", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.LastWriteTimeUtc >= crash)
            .OrderBy(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    private async Task<ServerLoaderDto?> DetectLoaderAsync(string serverName, CancellationToken ct)
    {
        try
        {
            return await _servers.DetectLoaderAsync(serverName, ct);
        }
        catch (Exception ex)
        {
            // Loader detection touches the server config on disk; a missing or unreadable one
            // must never block a diagnosis. The lines are simply omitted.
            _logger.LogDebug("Could not detect the loader for {Server}: {Message}", serverName, ex.Message);
            return null;
        }
    }
}
