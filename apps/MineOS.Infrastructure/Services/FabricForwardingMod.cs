using MineOS.Application.Dtos;

namespace MineOS.Infrastructure.Services;

/// <summary>
/// Knowledge about FabricProxy-Lite, the mod that gives a Fabric server the
/// ability to verify Velocity's modern forwarding. Paper implements that natively;
/// Fabric needs this one jar, which is the whole reason Fabric is a separate tier.
///
/// The decisions here are pure so they can be tested without Modrinth or a server.
/// </summary>
internal static class FabricForwardingMod
{
    /// <summary>Modrinth accepts the slug wherever it accepts a project id.</summary>
    internal const string ModrinthProjectId = "fabricproxy-lite";

    internal const string DisplayName = "FabricProxy-Lite";

    /// <summary>
    /// Recognises the mod's jar across the spellings it ships under
    /// ("FabricProxy-Lite-0.9.0.jar", "fabricproxy_lite.jar", …).
    ///
    /// Only enabled jars count. MineOS disables a mod by renaming it, and a
    /// disabled FabricProxy-Lite verifies nothing — treating it as present would
    /// report a spoofable server as secured, which is the one mistake this whole
    /// feature exists to prevent.
    /// </summary>
    internal static bool IsForwardingModJar(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = fileName.Trim().ToLowerInvariant();
        if (!name.EndsWith(".jar", StringComparison.Ordinal))
        {
            return false;
        }

        // Collapse the separators authors vary on so one comparison covers them all.
        var squashed = name.Replace("-", "").Replace("_", "").Replace(" ", "");
        return squashed.Contains("fabricproxylite", StringComparison.Ordinal);
    }

    /// <summary>
    /// Picks the newest release that suits this server, or null when none does.
    ///
    /// Returning null matters: installing a FabricProxy-Lite built for another
    /// Minecraft version leaves a server that looks secured and silently fails to
    /// verify anything, so "no suitable build" must surface as a refusal rather
    /// than a best guess.
    /// </summary>
    internal static ModrinthVersionDto? SelectVersion(
        IReadOnlyList<ModrinthVersionDto> versions, string? gameVersion)
    {
        if (versions is null || versions.Count == 0)
        {
            return null;
        }

        var candidates = versions.Where(v =>
            v.Loaders is null ||
            v.Loaders.Count == 0 ||
            v.Loaders.Any(l => string.Equals(l, "fabric", StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            candidates = candidates.Where(v =>
                v.GameVersions is not null &&
                v.GameVersions.Any(g => string.Equals(g, gameVersion, StringComparison.OrdinalIgnoreCase)));
        }

        return candidates.OrderByDescending(v => v.DatePublished).FirstOrDefault();
    }

    /// <summary>
    /// The project ids this version cannot run without.
    ///
    /// FabricProxy-Lite requires Fabric API, and a Fabric server whose mod has an
    /// unmet hard dependency does not start with a warning — it refuses to boot at
    /// all. Installing the mod without its dependencies is therefore worse than
    /// not offering the install.
    /// </summary>
    internal static IReadOnlyList<string> RequiredDependencyProjects(ModrinthVersionDto? version)
    {
        if (version?.Dependencies is null)
        {
            return Array.Empty<string>();
        }

        return version.Dependencies
            .Where(d => string.Equals(d.DependencyType, "required", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.ProjectId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The file to download from a version. Modrinth marks one file primary; the
    /// rest are sources and javadoc jars, which would install cleanly and do
    /// nothing.
    /// </summary>
    internal static ModrinthVersionFileDto? SelectFile(ModrinthVersionDto? version)
    {
        if (version?.Files is null || version.Files.Count == 0)
        {
            return null;
        }
        return version.Files.FirstOrDefault(f => f.Primary)
               ?? version.Files.FirstOrDefault(f => f.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
    }
}
