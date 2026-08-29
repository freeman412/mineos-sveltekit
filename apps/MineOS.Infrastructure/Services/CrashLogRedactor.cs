// apps/MineOS.Infrastructure/Services/CrashLogRedactor.cs
using System.Text.RegularExpressions;
using MineOS.Application.Dtos;

namespace MineOS.Infrastructure.Services;

/// <summary>
/// Strips secrets and personal data out of crash reports and log tails before
/// they leave the machine. Pure and deterministic so it can be tested directly.
/// </summary>
public static partial class CrashLogRedactor
{
    // Mandatory rules. These protect other people's data — players' addresses
    // and third-party credentials — and are never switched off by a setting.
    //
    // The leading (?<!-) guards against over-redaction: a four-group dotted
    // version suffix inside a mod filename (e.g. "jei-1.20.1-15.2.0.27.jar")
    // is shaped exactly like an IPv4 address once you look at just the last
    // four dot-separated numbers. Real player addresses in these logs are
    // always preceded by "/", "[", or whitespace, never by a hyphen, so
    // excluding a hyphen immediately before the match keeps the mod filename
    // intact while still catching "[/192.168.1.50:52344]" and
    // "from /10.0.0.4:25565".
    [GeneratedRegex(@"(?<!-)/?\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b")]
    private static partial Regex IpAddress();

    // IPv6, the other half of the mandatory "ip-address" rule. Reported under the same rule
    // name as IPv4 because it protects exactly the same thing: a third party's address.
    //
    // Two forms, in this order: bracketed with an optional port, as vanilla writes a player's
    // address ("Steve[/[2001:db8::1]:52344] logged in"), then bare.
    //
    // The shape is deliberately strict rather than "hex groups separated by colons": a log is
    // full of "[12:04:31]" timestamps and "Foo.java:123" frames, which that loose shape would
    // eat. A real address is either the full eight groups or contains a "::" elision, and
    // neither of those describes a timestamp. The lookbehind on the bare form stops a match
    // starting in the middle of an address or a "1.50:52344" port suffix.
    [GeneratedRegex(@"/?\[(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:(?:[0-9A-Fa-f]{1,4}:)*(?:(?:\d{1,3}\.){3}\d{1,3}|[0-9A-Fa-f]{1,4}))?|::(?:[0-9A-Fa-f]{1,4}:)*(?:(?:\d{1,3}\.){3}\d{1,3}|[0-9A-Fa-f]{1,4}))\](?::\d{1,5})?|(?<![0-9A-Fa-f:.])/?(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:(?:[0-9A-Fa-f]{1,4}:)*(?:(?:\d{1,3}\.){3}\d{1,3}|[0-9A-Fa-f]{1,4}))?|::(?:[0-9A-Fa-f]{1,4}:)*(?:(?:\d{1,3}\.){3}\d{1,3}|[0-9A-Fa-f]{1,4}))")]
    private static partial Regex Ipv6Address();

    // Deliberately NOT matching bare base64 runs: "[A-Za-z0-9+/]{40,}" also
    // matches filesystem paths, which would swallow the mod filename that is
    // usually the diagnosis. Labelled and structured tokens only.
    [GeneratedRegex(@"(?:sk-[A-Za-z0-9\-_]{16,}|Bot\s+[A-Za-z0-9._\-]{24,}|[A-Za-z0-9_\-]{20,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{20,})")]
    private static partial Regex SecretToken();

    // Not anchored to line start: credentials appear mid-line in log output.
    [GeneratedRegex(@"(?i)\b([\w.\-]*(?:password|secret|token|api[_.\-]?key|server-ip)\s*[=:])\s*\S+")]
    private static partial Regex CredentialLine();

    [GeneratedRegex(@"\b[\w.\-+]+@[\w\-]+\.[\w.\-]+\b")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex Uuid();

    // Optional rules.
    // Keeps the tail after the user directory: the mod filename is the diagnosis.
    [GeneratedRegex(@"(?:/home/[^/\s]+|/Users/[^/\s]+|[A-Za-z]:\\Users\\[^\\\s]+)")]
    private static partial Regex HomePath();

    public static RedactionResult Redact(string input, RedactionOptions options)
    {
        if (string.IsNullOrEmpty(input)) return new RedactionResult(string.Empty, Array.Empty<string>());

        var applied = new List<string>();
        var text = input;

        // Order matters: credential lines first, so a password that also looks
        // like a token is caught by the more specific rule.
        text = Apply(text, CredentialLine(), "$1 <redacted-secret>", "credential-line", applied);
        text = Apply(text, SecretToken(), "<redacted-secret>", "secret-token", applied);
        text = Apply(text, EmailAddress(), "<email>", "email", applied);
        // IPv6 first: an IPv4-mapped address ("::ffff:203.0.113.9") must be taken whole
        // rather than having its trailing dotted quad carved out from under it.
        text = Apply(text, Ipv6Address(), "<ip>", "ip-address", applied);
        text = Apply(text, IpAddress(), "<ip>", "ip-address", applied);

        if (options.RedactPaths)
        {
            text = Apply(text, HomePath(), "<path>", "home-path", applied);
        }

        if (options.RedactPlayerNames)
        {
            text = Apply(text, Uuid(), "<uuid>", "player-uuid", applied);
            text = RedactPlayers(text, options.KnownPlayers, applied);
        }

        return new RedactionResult(text, applied);
    }

    private static string Apply(string text, Regex pattern, string replacement, string ruleName, List<string> applied)
    {
        if (!pattern.IsMatch(text)) return text;
        // Two patterns share the "ip-address" rule name; the report lists a rule once.
        if (!applied.Contains(ruleName)) applied.Add(ruleName);
        return pattern.Replace(text, replacement);
    }

    private static string RedactPlayers(string text, IReadOnlyCollection<string> knownPlayers, List<string> applied)
    {
        // Longest first, so "SteveTheBuilder" is not partly replaced by "Steve".
        var ordered = knownPlayers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(name => name.Length)
            .ToList();

        var index = 1;
        var fired = false;
        foreach (var name in ordered)
        {
            var pattern = new Regex($@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase);
            if (!pattern.IsMatch(text)) continue;

            // A stable pseudonym per player keeps "the same player did this three
            // times" readable without revealing who they are.
            text = pattern.Replace(text, $"<player{index}>");
            index++;
            fired = true;
        }

        if (fired) applied.Add("player-name");
        return text;
    }
}
