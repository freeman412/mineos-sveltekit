namespace MineOS.Application;

/// <summary>
/// Pure helpers for server-software update detection: parsing installed
/// versions out of jar names and comparing dotted version strings. Kept
/// framework-free so the logic is unit-testable; the background checker in
/// Infrastructure supplies the upstream data.
/// </summary>
public static class UpdateDetection
{
    /// <summary>
    /// Compare dotted/numeric version strings segment-wise ("21.1.227" vs
    /// "21.1.30" → positive). Non-numeric segments compare ordinally.
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        var aParts = Split(a);
        var bParts = Split(b);
        var len = Math.Max(aParts.Length, bParts.Length);
        for (var i = 0; i < len; i++)
        {
            var ap = i < aParts.Length ? aParts[i] : "0";
            var bp = i < bParts.Length ? bParts[i] : "0";
            if (int.TryParse(ap, out var an) && int.TryParse(bp, out var bn))
            {
                if (an != bn) return an.CompareTo(bn);
            }
            else
            {
                var cmp = string.Compare(ap, bp, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
        }
        return 0;

        static string[] Split(string v) =>
            v.Trim().TrimStart('v', 'V').Split('.', '-', '+');
    }

    /// <summary>Pick the highest version from a list (null when empty).</summary>
    public static string? PickLatest(IEnumerable<string> versions)
    {
        string? best = null;
        foreach (var v in versions)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            if (best is null || CompareVersions(v, best) > 0) best = v;
        }
        return best;
    }

    /// <summary>
    /// Parse a Paper jar name like "paper-1.21.1-133.jar" into its Minecraft
    /// version and build number. Null when the name doesn't carry both.
    /// </summary>
    public static (string McVersion, int Build)? TryParsePaperJar(string? jarFile)
    {
        if (string.IsNullOrWhiteSpace(jarFile)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileName(jarFile.Trim()),
            @"^paper-(?<mc>\d+\.\d+(?:\.\d+)?)-(?<build>\d+)\.jar$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups["build"].Value, out var build)) return null;
        return (match.Groups["mc"].Value, build);
    }

    /// <summary>
    /// Split a Forge full version "1.20.1-47.2.0" into (mc, forge). Null when
    /// the string isn't in that two-part shape.
    /// </summary>
    public static (string McVersion, string ForgeVersion)? TrySplitForgeVersion(string? fullVersion)
    {
        if (string.IsNullOrWhiteSpace(fullVersion)) return null;
        var idx = fullVersion.IndexOf('-');
        if (idx <= 0 || idx >= fullVersion.Length - 1) return null;
        var mc = fullVersion[..idx];
        var forge = fullVersion[(idx + 1)..];
        if (!mc.Contains('.') || !char.IsDigit(mc[0]) || !char.IsDigit(forge[0])) return null;
        return (mc, forge);
    }

    /// <summary>
    /// The Minecraft line a NeoForge version targets: "21.1.227" → "21.1"
    /// (NeoForge major.minor tracks MC 1.major.minor). Null when unparseable.
    /// </summary>
    public static string? NeoForgeMcLine(string? neoForgeVersion)
    {
        if (string.IsNullOrWhiteSpace(neoForgeVersion)) return null;
        var parts = neoForgeVersion.Trim().Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _)) return null;
        return $"{parts[0]}.{parts[1]}";
    }
}
