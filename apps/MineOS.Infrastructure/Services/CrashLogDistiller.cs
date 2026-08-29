// apps/MineOS.Infrastructure/Services/CrashLogDistiller.cs
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MineOS.Application.Dtos;

namespace MineOS.Infrastructure.Services;

/// <summary>
/// Turns a whole Minecraft server session log into a small, dense summary that keeps the
/// diagnostic signal from the entire file rather than just the last few seconds of it.
///
/// A modded crash is usually *caused* by something logged half an hour before the stack trace
/// that finally killed the server — a mod failing capability registration, a config error during
/// load. Sending a tail shows the symptom and hides the cause; sending the whole file buries the
/// cause in chunk-save spam. So this scans everything and keeps only: the session header, every
/// distinct problem event with its stack trace (repeats collapsed into a count), landmark lines,
/// and the verbatim tail.
///
/// Pure, deterministic, streaming and bounded in memory: the input is enumerated exactly once,
/// forward, and only a header buffer, a fixed-size tail ring and the signature dictionary are
/// retained. A 500 MB log must never become a 500 MB allocation.
/// </summary>
public static partial class CrashLogDistiller
{
    // ---- line grammar -----------------------------------------------------------------------
    //
    // "[12:04:31] [Server thread/ERROR] [thermal/]: Exception ticking entity"
    // "[12:04:31] [Server thread/ERROR]: Exception ticking entity"
    // "[12:04:31 ERROR]: legacy form"
    //
    // Anything that does not start with a bracketed timestamp is a *continuation* of the line
    // above: "\tat ...", "Caused by: ...", "... 12 more", wrapped messages. That grouping is what
    // lets a stack trace survive deduplication in one piece.
    [GeneratedRegex(@"^\[(?<ts>\d{1,4}[-:.\d]*(?:[ T]\d{1,2}[:.\d]*)?)(?:\s+(?<lvl>[A-Za-z]{4,7}))?\]\s*(?<rest>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LogLineStart();

    // One or more leading bracketed sections, then ": " and the message. The bracket run is only
    // matched at the start, so a "]:" inside the message itself cannot confuse it.
    [GeneratedRegex(@"^(?<sections>(?:\[[^\]]*\]\s*)+):\s?(?<msg>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionedMessage();

    // Leftmost match wins, which is what we want: the thread section carries the level, and a mod
    // id later in the line that happens to contain "error" cannot override it.
    [GeneratedRegex(@"\b(FATAL|ERROR|WARNING|WARN|INFO|DEBUG|TRACE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelToken();

    // Landmarks are kept regardless of level: they anchor the model in the session's shape
    // (what loaded, when the world came up, whether the shutdown was clean).
    [GeneratedRegex(@"(Loading \d+ mods|Forge mod loading|Preparing level|Done \(.*\)! For help|Starting minecraft server|Stopping server)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LandmarkPattern();

    // ---- signature normalisation ------------------------------------------------------------
    //
    // Used ONLY to decide that two events are the same fault. The text that gets emitted is
    // always the first occurrence's original, so the model reads a real stack trace with real
    // numbers rather than "Entity <n> at (<n>, <n>, <n>)".
    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex UuidValue();

    // Deliberately anchored to real absolute-path roots. A greedy "anything with a slash" pattern
    // eats java package names and mod ids, which are exactly the tokens that distinguish faults.
    [GeneratedRegex(@"(?:[A-Za-z]:\\[^\s""']*|/(?:home|Users|opt|var|usr|srv|mnt|tmp|etc|data|app)/[^\s""']*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePath();

    // The <ver> alternative is matched first and deliberately left ALONE by the evaluator below.
    // Collapsing "1.20.1" and "47.2.0" to <n> would merge two genuinely different faults that
    // differ only by a mod or loader version — exactly the distinction an admin needs to see.
    // Three or more dot-separated numeric groups is the shape of a version and nothing else;
    // coordinates and entity ids never reach three groups.
    [GeneratedRegex(@"(?<ver>\b\d+(?:\.\d+){2,}\b)|0[xX][0-9a-fA-F]+|\d+(?:\.\d+)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumericValue();

    private static string ReplaceNumeric(Match match) => match.Groups["ver"].Success ? match.Value : "<n>";

    private const int MaxLandmarks = 200;
    private const string Elision = "  ⤷ ";

    public static LogDistillation Distill(IEnumerable<string> lines, LogDistillerOptions options)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);

        var scan = new ScanState(options);
        foreach (var line in lines)
        {
            if (!scan.Accept(line ?? string.Empty)) break;
        }

        scan.Finish();
        return Render(scan, options);
    }

    // =========================================================================================
    // Parsing
    // =========================================================================================

    private readonly record struct ParsedLine(bool IsNewEntry, string Timestamp, string Level, string Message);

    private static ParsedLine Parse(string raw)
    {
        var start = LogLineStart().Match(raw);
        if (!start.Success) return new ParsedLine(false, string.Empty, string.Empty, raw);

        var timestamp = start.Groups["ts"].Value;
        var rest = start.Groups["rest"].Value;

        // Legacy "[12:04:31 ERROR]: message" — the level sits inside the timestamp bracket.
        if (start.Groups["lvl"].Success)
        {
            var legacyLevel = NormaliseLevel(start.Groups["lvl"].Value);
            return new ParsedLine(true, timestamp, legacyLevel, rest.TrimStart(':', ' ', '\t'));
        }

        var sectioned = SectionedMessage().Match(rest);
        if (!sectioned.Success)
        {
            // Timestamped but shaped oddly. Still a new entry, just with no level we can trust.
            return new ParsedLine(true, timestamp, string.Empty, rest);
        }

        var token = LevelToken().Match(sectioned.Groups["sections"].Value);
        var level = token.Success ? NormaliseLevel(token.Value) : string.Empty;
        return new ParsedLine(true, timestamp, level, sectioned.Groups["msg"].Value);
    }

    private static string NormaliseLevel(string value)
    {
        var upper = value.ToUpperInvariant();
        return upper switch
        {
            "FATAL" => "FATAL",
            "ERROR" => "ERROR",
            "WARN" or "WARNING" => "WARN",
            "INFO" => "INFO",
            "DEBUG" => "DEBUG",
            "TRACE" => "TRACE",
            _ => string.Empty
        };
    }

    private static int SeverityOf(string level) => level switch
    {
        "FATAL" => 3,
        "ERROR" => 2,
        "WARN" => 1,
        _ => 0
    };

    private static string Normalise(string text)
    {
        // Order matters: UUIDs and paths are consumed before the numeric rule can shred them.
        var value = UuidValue().Replace(text, "<uuid>");
        value = AbsolutePath().Replace(value, "<path>");
        value = NumericValue().Replace(value, ReplaceNumeric);
        return value.Trim();
    }

    // =========================================================================================
    // Scan state — the only thing held across the stream
    // =========================================================================================

    private readonly record struct LineEntry(long PushIndex, string Text, int Ordinal);

    // Ordinal semantics on a buffered line:
    //   >= 0  the line belongs to stored problem event with that id
    //   -1    ordinary line (INFO/DEBUG/unknown)
    //   -2    a problem event that could not be stored (MaxDistinctEvents reached)
    private const int OrdinaryLine = -1;
    private const int UnstoredEvent = -2;

    private sealed class LogEvent
    {
        public int Id;
        public string Level = string.Empty;
        public int Severity;
        public string FirstTimestamp = string.Empty;
        public string LastTimestamp = string.Empty;
        public long Count;
        public List<string> Lines = new();
        public int ExtraFrames;
        public bool Rendered;
    }

    private sealed class ScanState
    {
        private readonly LogDistillerOptions _options;
        private readonly int _eventLineCap;
        private readonly LineEntry[] _ring;
        private readonly HashSet<string> _landmarkSeen = new(StringComparer.Ordinal);

        private readonly List<string> _current = new();
        private readonly StringBuilder _signature = new();
        private bool _active;
        private int _currentSeverity;
        private string _currentLevel = string.Empty;
        private string _currentTimestamp = string.Empty;
        private int _currentFrames;
        private int _currentDropped;
        private bool _currentLandmark;
        private string _currentLandmarkText = string.Empty;

        private int _ringNext;
        private long _pushed;

        public ScanState(LogDistillerOptions options)
        {
            _options = options;
            _eventLineCap = Math.Max(options.TailLines, options.MaxStackFrames) + 1;
            _ring = new LineEntry[Math.Max(0, options.TailLines)];
        }

        public LogDistillerOptions Options => _options;
        public long LinesScanned { get; private set; }
        public long BytesScanned { get; private set; }
        public bool LandmarkCaptureDisabled { get; private set; }
        public long OrdinaryLinesScanned { get; private set; }
        public long EventOccurrences { get; private set; }
        public int EventsOmitted { get; private set; }
        public int LandmarksOmitted { get; private set; }

        public List<LineEntry> Header { get; } = new();
        public List<LogEvent> Events { get; } = new();
        public List<LineEntry> Landmarks { get; } = new();
        public Dictionary<string, LogEvent> Signatures { get; } = new(StringComparer.Ordinal);

        public int RingCount { get; private set; }
        public long TailStartIndex => _pushed - RingCount;

        public bool Accept(string raw)
        {
            BytesScanned += (long)raw.Length + 1;
            LinesScanned++;

            // Past the byte budget we stop running the landmark regex — the only genuinely
            // per-line optional work, and the only thing that would make a multi-gigabyte scan
            // expensive. Everything else keeps going: the session header is bounded by
            // HeaderLines in total (~60 lines, then never again) and is far too valuable on a
            // modded crash to sacrifice, and the ring buffer and event dictionary are already
            // O(1) in the file size. We never stop reading: on a multi-gigabyte log the crash is
            // at the END, and quitting at the front would diagnose the server's startup instead
            // of the thing that killed it. Reading on costs seconds of I/O.
            if (!LandmarkCaptureDisabled && BytesScanned > _options.LandmarkScanByteLimit)
            {
                LandmarkCaptureDisabled = true;
            }

            var parsed = Parse(raw);
            if (parsed.IsNewEntry)
            {
                Flush();
                Start(parsed, raw);
            }
            else
            {
                Continue(raw);
            }

            return true;
        }

        private void Start(ParsedLine parsed, string raw)
        {
            _active = true;
            _current.Clear();
            _current.Add(raw);
            _currentLevel = parsed.Level;
            _currentSeverity = SeverityOf(parsed.Level);
            _currentTimestamp = parsed.Timestamp;
            _currentFrames = 0;
            _currentDropped = 0;

            var landmark = !LandmarkCaptureDisabled && LandmarkPattern().IsMatch(parsed.Message);
            _currentLandmark = landmark;
            _currentLandmarkText = landmark ? raw : string.Empty;

            if (_currentSeverity > 0)
            {
                _signature.Clear();
                _signature.Append(parsed.Level).Append('\n').Append(Normalise(parsed.Message));
            }
        }

        private void Continue(string raw)
        {
            if (!_active)
            {
                // A log that starts mid-stack still has to go somewhere.
                Start(new ParsedLine(true, string.Empty, string.Empty, raw), raw);
                return;
            }

            if (_current.Count < _eventLineCap)
            {
                _current.Add(raw);
            }
            else
            {
                // A single event with an unbounded number of continuation lines must not be able
                // to grow the buffer without limit.
                _currentDropped++;
            }

            if (_currentSeverity > 0 && _currentFrames < _options.MaxStackFrames)
            {
                _currentFrames++;
                _signature.Append('\n').Append(Normalise(raw));
            }
        }

        public void Finish() => Flush();

        private void Flush()
        {
            if (!_active) return;
            _active = false;

            var ordinal = OrdinaryLine;
            if (_currentSeverity > 0)
            {
                EventOccurrences++;
                var key = _signature.ToString();
                if (Signatures.TryGetValue(key, out var existing))
                {
                    existing.Count++;
                    existing.LastTimestamp = _currentTimestamp;
                    ordinal = existing.Id;
                }
                else if (Signatures.Count < _options.MaxDistinctEvents)
                {
                    var keep = Math.Min(_current.Count, 1 + _options.MaxStackFrames);
                    var created = new LogEvent
                    {
                        Id = Events.Count,
                        Level = _currentLevel,
                        Severity = _currentSeverity,
                        FirstTimestamp = _currentTimestamp,
                        LastTimestamp = _currentTimestamp,
                        Count = 1,
                        Lines = _current.GetRange(0, keep),
                        ExtraFrames = _current.Count - keep + _currentDropped
                    };
                    Signatures[key] = created;
                    Events.Add(created);
                    ordinal = created.Id;
                }
                else
                {
                    EventsOmitted++;
                    ordinal = UnstoredEvent;
                }
            }
            else
            {
                OrdinaryLinesScanned += _current.Count + _currentDropped;
            }

            if (_currentLandmark) RecordLandmark(ordinal);
            foreach (var line in _current) Push(line, ordinal);
        }

        private void RecordLandmark(int ordinal)
        {
            // Problem-event landmarks are already represented in the events section; re-listing
            // them here would just duplicate text.
            if (ordinal != OrdinaryLine) return;
            if (Landmarks.Count >= MaxLandmarks)
            {
                LandmarksOmitted++;
                return;
            }

            // "Preparing level" can fire on every dimension load; keep one of each distinct line.
            if (!_landmarkSeen.Add(Normalise(_currentLandmarkText)))
            {
                LandmarksOmitted++;
                return;
            }

            Landmarks.Add(new LineEntry(_pushed, _currentLandmarkText, ordinal));
        }

        private void Push(string text, int ordinal)
        {
            var entry = new LineEntry(_pushed, text, ordinal);
            if (Header.Count < _options.HeaderLines) Header.Add(entry);

            if (_ring.Length > 0)
            {
                _ring[_ringNext] = entry;
                _ringNext = (_ringNext + 1) % _ring.Length;
                if (RingCount < _ring.Length) RingCount++;
            }

            _pushed++;
        }

        /// <summary>The buffered tail in original order, oldest first.</summary>
        public IEnumerable<LineEntry> Tail()
        {
            if (_ring.Length == 0) yield break;
            var start = (_ringNext - RingCount + _ring.Length) % _ring.Length;
            for (var i = 0; i < RingCount; i++)
            {
                yield return _ring[(start + i) % _ring.Length];
            }
        }
    }

    // =========================================================================================
    // Rendering
    // =========================================================================================

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static LogDistillation Render(ScanState scan, LogDistillerOptions options)
    {
        var budget = Math.Max(0, options.MaxOutputCharacters);
        var sb = new StringBuilder();
        long emitted = 0;

        int Used() => sb.Length;

        bool TryLine(string text, int cap)
        {
            var add = sb.Length == 0 ? text.Length : text.Length + 1;
            if (sb.Length + add > cap) return false;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(text);
            return true;
        }

        // The tail's budget is reserved before anything else is written, so a flood of WARN events
        // can never starve the immediate pre-crash context out of the output. The provisional pass
        // assumes every stored event will be rendered; the real pass below uses the actual flags
        // and may spend more than the reservation if earlier sections left room.
        var provisional = BuildTail(scan, useRenderedFlags: false);
        var reserve = Math.Min(budget / 2, Length(provisional));
        var sectionBudget = Math.Max(0, budget - reserve);

        var landmarkNote = scan.LandmarkCaptureDisabled
            ? $", whole file read ({N(scan.BytesScanned)} bytes); the session header below is "
              + $"complete, but landmark matching was disabled past {N(options.LandmarkScanByteLimit)} bytes"
            : string.Empty;
        TryLine(
            $"=== distilled log: {N(scan.LinesScanned)} lines scanned, {N(scan.Signatures.Count)} distinct problem events, "
            + $"{N(scan.EventOccurrences)} occurrences{landmarkNote} ===",
            sectionBudget);

        // --- session header ---------------------------------------------------------------
        // Lines belonging to a stored problem event are left out here: they are reproduced in
        // full, with their counts, in the events sections below. The gap is marked inline, the
        // same way the tail marks it — the heading promises the first N lines of the session, so
        // an unmarked hole under it would read as a contiguous verbatim block that it is not.
        var headerLines = new List<string>();
        var headerMarkers = new List<bool>();
        long headerCollapsed = 0;

        void FlushHeaderCollapsed()
        {
            if (headerCollapsed == 0) return;
            headerLines.Add($"{Elision}{N(headerCollapsed)} repeated lines omitted (shown below)");
            headerMarkers.Add(true);
            headerCollapsed = 0;
        }

        foreach (var entry in scan.Header)
        {
            if (entry.Ordinal >= 0) { headerCollapsed++; continue; }
            FlushHeaderCollapsed();
            headerLines.Add(entry.Text);
            headerMarkers.Add(false);
        }

        FlushHeaderCollapsed();

        var headerWritten = AppendBlock(
            TryLine, $"=== session start (first {N(scan.Header.Count)} lines) ===", headerLines,
            Math.Min(sectionBudget, budget == 0 ? 0 : Math.Max(budget / 4, 0)));
        // Marker lines are not source lines; they must not count towards what was emitted.
        var headerEmitted = headerWritten - headerMarkers.Take((int)headerWritten).Count(m => m);
        emitted += headerEmitted;

        // --- landmarks ---------------------------------------------------------------------
        var tailStart = scan.TailStartIndex;
        var landmarkLines = scan.Landmarks
            .Where(l => l.PushIndex >= scan.Header.Count && l.PushIndex < tailStart)
            .Select(l => l.Text)
            .ToList();
        if (scan.LandmarksOmitted > 0)
        {
            landmarkLines.Add($"--- {N(scan.LandmarksOmitted)} repeated or further landmark lines omitted ---");
        }

        emitted += AppendBlock(TryLine, "=== session landmarks ===", landmarkLines, sectionBudget);

        // --- what was thrown away ----------------------------------------------------------
        // Never let the model reason from absence: "there were no earlier lines" and "earlier
        // lines were dropped" are completely different claims.
        var ordinaryKept = headerEmitted + provisional.OrdinaryLines;
        var ordinaryOmitted = Math.Max(0, scan.OrdinaryLinesScanned - ordinaryKept);
        if (ordinaryOmitted > 0)
        {
            TryLine($"--- {N(ordinaryOmitted)} INFO/DEBUG lines omitted ---", sectionBudget);
        }

        // --- problem events, highest severity first ----------------------------------------
        foreach (var severity in new[] { 3, 2, 1 })
        {
            emitted += AppendEvents(scan, TryLine, Used, severity, sectionBudget);
        }

        if (scan.EventsOmitted > 0)
        {
            TryLine(
                $"--- {N(scan.EventsOmitted)} further problem events omitted (distinct-event limit of "
                + $"{N(options.MaxDistinctEvents)} reached) ---",
                sectionBudget);
        }

        // --- verbatim tail -----------------------------------------------------------------
        var tail = BuildTail(scan, useRenderedFlags: true);
        var tailBudget = budget;
        if (tail.Lines.Count > 0)
        {
            var heading = $"=== end of session: last {N(options.TailLines)} lines (verbatim) ===";
            emitted += AppendTail(TryLine, heading, tail, tailBudget, Used());
        }

        var text = sb.ToString();
        var stats = new LogDistillationStats(
            scan.LinesScanned,
            scan.BytesScanned,
            scan.LandmarkCaptureDisabled,
            scan.Signatures.Count,
            scan.EventOccurrences,
            scan.EventsOmitted,
            Math.Max(0, scan.LinesScanned - emitted));

        return new LogDistillation(text, stats);
    }

    private static long AppendBlock(Func<string, int, bool> tryLine, string heading, List<string> lines, int cap)
    {
        if (lines.Count == 0) return 0;

        var headingWritten = false;
        long written = 0;
        foreach (var line in lines)
        {
            if (!headingWritten)
            {
                if (!tryLine(heading, cap)) return written;
                headingWritten = true;
            }

            if (!tryLine(line, cap)) return written;
            written++;
        }

        return written;
    }

    private static long AppendEvents(
        ScanState scan, Func<string, int, bool> tryLine, Func<int> used, int severity, int cap)
    {
        var events = scan.Events.Where(e => e.Severity == severity).ToList();
        if (events.Count == 0) return 0;

        var level = events[0].Level;
        var occurrences = events.Sum(e => e.Count);
        var heading = $"=== {level}: {N(events.Count)} distinct, {N(occurrences)} occurrences ===";

        var headingWritten = false;
        long written = 0;
        var index = 0;
        for (; index < events.Count; index++)
        {
            var ev = events[index];
            var block = new List<string>(ev.Lines);
            if (ev.ExtraFrames > 0) block.Add($"{Elision}{N(ev.ExtraFrames)} more stack frames");
            if (ev.Count > 1)
            {
                var range = ev.FirstTimestamp == ev.LastTimestamp
                    ? string.Empty
                    : $" ({ev.FirstTimestamp} – {ev.LastTimestamp})";
                block.Add($"{Elision}repeated {N(ev.Count)} times{range}");
            }

            // All-or-nothing per event: half a stack trace is worse than a marked omission.
            var cost = block.Sum(l => l.Length + 1) + (headingWritten ? 0 : heading.Length + 1);
            if (used() + cost > cap) break;

            if (!headingWritten)
            {
                if (!tryLine(heading, cap)) break;
                headingWritten = true;
            }

            var fits = true;
            foreach (var line in block)
            {
                if (!tryLine(line, cap)) { fits = false; break; }
            }

            if (!fits) break;
            ev.Rendered = true;
            written += ev.Lines.Count;
        }

        var dropped = events.Count - index;
        if (dropped > 0 && headingWritten)
        {
            tryLine($"--- {N(dropped)} further {level} events omitted (budget) ---", cap);
        }
        else if (dropped > 0)
        {
            tryLine($"--- {N(dropped)} {level} events omitted (budget) ---", cap);
        }

        return written;
    }

    private sealed class TailBlock
    {
        public List<string> Lines = new();
        public long OrdinaryLines;
        public long EventLines;
    }

    private static int Length(TailBlock block) => block.Lines.Sum(l => l.Length + 1);

    /// <summary>
    /// The last <c>TailLines</c> lines in order. Lines that are already reproduced verbatim in the
    /// events section are replaced by a counted marker rather than repeated — the tail exists for
    /// ordering and immediate pre-crash context, not to print the same stack 300 more times.
    /// </summary>
    private static TailBlock BuildTail(ScanState scan, bool useRenderedFlags)
    {
        var block = new TailBlock();
        long collapsed = 0;

        void FlushCollapsed()
        {
            if (collapsed == 0) return;
            block.Lines.Add($"{Elision}{N(collapsed)} repeated lines omitted (shown above)");
            collapsed = 0;
        }

        foreach (var entry in scan.Tail())
        {
            // Already printed verbatim in the session header.
            if (entry.PushIndex < scan.Header.Count) continue;

            var hidden = entry.Ordinal >= 0
                && (!useRenderedFlags || scan.Events[entry.Ordinal].Rendered);
            if (hidden)
            {
                collapsed++;
                continue;
            }

            FlushCollapsed();
            block.Lines.Add(entry.Text);
            if (entry.Ordinal == OrdinaryLine) block.OrdinaryLines++; else block.EventLines++;
        }

        FlushCollapsed();
        return block;
    }

    private static long AppendTail(
        Func<string, int, bool> tryLine, string heading, TailBlock tail, int cap, int usedNow)
    {
        var lines = tail.Lines;

        // Trim from the front if it will not fit: the newest lines are the ones worth keeping.
        var keep = lines.Count;
        var limit = Math.Max(0, cap - usedNow - 80);
        var total = heading.Length + 1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            total += lines[i].Length + 1;
            if (total > limit)
            {
                keep = lines.Count - 1 - i;
                break;
            }
        }

        var dropped = lines.Count - keep;
        var headingWritten = false;
        long written = 0;

        // Not one tail line fits. The marker still has to go out: an unmarked absence tells the
        // reader "nothing was here", which is the one conclusion this component exists to prevent.
        // If even the marker will not fit, that is the honest end of the budget.
        if (keep == 0)
        {
            tryLine($"--- {N(dropped)} earlier tail lines omitted (budget) ---", cap);
            return 0;
        }

        for (var i = lines.Count - keep; i < lines.Count; i++)
        {
            if (!headingWritten)
            {
                if (!tryLine(heading, cap)) return written;
                headingWritten = true;
                if (dropped > 0)
                {
                    tryLine($"--- {N(dropped)} earlier tail lines omitted (budget) ---", cap);
                }
            }

            if (!tryLine(lines[i], cap)) return written;
            written++;
        }

        return written;
    }
}
