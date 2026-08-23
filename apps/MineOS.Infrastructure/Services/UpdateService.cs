using System.IO.Compression;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

/// <summary>
/// Detects and applies server software updates (issue #83). Detection is
/// filesystem-first: the server's own jar filename (or, for Bedrock, the
/// version recorded by the last guided apply) is compared against the profile
/// catalog that ProfileService already fetches and caches from upstream.
///
/// The [updates] section of server.config is owned here — deliberately outside
/// ServerConfigDto so the full-replace config writer cannot wipe it (it carries
/// the section through untouched instead).
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private const string UpdatesSection = "updates";

    // Supported families and how to read family + version + build out of a jar
    // filename. Deliberately excludes modded loaders: swapping a loader under a
    // pile of mods is a manual job (issue #38 territory for mod updates).
    private static readonly (string Family, Regex Pattern)[] JarPatterns =
    {
        ("paper", new Regex(@"^paper-(?<mc>\d+(?:\.\d+)*)-(?<build>\d+)\.jar$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("vanilla", new Regex(@"^vanilla-(?<mc>\d+(?:\.\d+)*)\.jar$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("velocity", new Regex(@"^velocity-(?<v>\d+(?:\.\d+)*)(?:-(?<build>\d+))?\.jar$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("bungeecord", new Regex(@"^bungeecord-build-(?<build>\d+)\.jar$", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    };

    private static readonly Regex[] UnsupportedLoaderPatterns =
    [
        new(@"^forge-", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^neoforge-", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^fabric-server-mc\.", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^fabric-loader-", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^quilt-server-", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    public const string ModeNotify = "notify";
    public const string ModeOff = "off";
    public const string ModeIgnoreCurrent = "ignore-current";

    private readonly IServerService _serverService;
    private readonly IProfileService _profileService;
    private readonly IProcessManager _processManager;
    private readonly HostOptions _options;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(
        IServerService serverService,
        IProfileService profileService,
        IProcessManager processManager,
        IOptions<HostOptions> options,
        ILogger<UpdateService> logger)
    {
        _serverService = serverService;
        _profileService = profileService;
        _processManager = processManager;
        _options = options.Value;
        _logger = logger;
    }

    private sealed record Identity(
        string? Family,
        string? Version,
        int? Build,
        string? JarFileName,
        string? UnsupportedReason);

    /// <summary>One concrete thing the user could install.</summary>
    private sealed record Offer(string ProfileId, string Version, int? Build);

    private string GetServerPath(string name) =>
        Path.Combine(_options.BaseDirectory, _options.ServersPathSegment, name);

    // ------------------------------------------------------------------
    // Status / detection
    // ------------------------------------------------------------------

    public async Task<ServerUpdateStatusDto> GetUpdateStatusAsync(string name, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(name);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        var settings = await ReadUpdatesSectionAsync(serverPath, cancellationToken);
        var mode = NormalizeMode(settings.GetValueOrDefault("mode"));
        var identity = await ResolveIdentityAsync(name, serverPath, cancellationToken);
        var offers = identity?.Family is null || identity.UnsupportedReason is not null
            ? null
            : await ComputeOffersAsync(identity, cancellationToken);

        var ignoredKey = NullIfEmpty(settings.GetValueOrDefault("ignored_key"));

        bool updateAvailable;
        Offer? badgingOffer;
        if (!identitySupported(identity) || offers is null || mode == ModeOff)
        {
            updateAvailable = false;
            badgingOffer = null;
        }
        else
        {
            // Build-based families badge only on the safe same-version bump — a
            // version jump never badges on its own (it stays an opt-in offer in
            // the dialog). Vanilla has no per-version builds, so its jump IS
            // the update and drives the badge.
            badgingOffer = identity!.Family == "vanilla" ? offers.Jump : offers.BuildBump;
            updateAvailable = badgingOffer is not null && OfferKey(badgingOffer) != ignoredKey;
        }

        return new ServerUpdateStatusDto(
            Supported: identitySupported(identity),
            Reason: identity?.UnsupportedReason ?? (identity?.Family is null ? "No compatible server software was detected on this server." : null),
            Mode: mode,
            Family: identity?.Family,
            UpdateAvailable: updateAvailable,
            CurrentVersion: identity?.Version,
            CurrentBuild: identity?.Build,
            LatestBuildVersion: offers?.BuildBump?.Version,
            LatestBuildNumber: offers?.BuildBump?.Build,
            LatestBuildProfileId: offers?.BuildBump?.ProfileId,
            JumpAvailable: offers?.Jump is not null,
            JumpVersion: offers?.Jump?.Version,
            JumpProfileId: offers?.Jump?.ProfileId,
            IgnoredUpdateKey: ignoredKey);

        static bool identitySupported(Identity? identity) =>
            identity is not null && identity.Family is not null && identity.UnsupportedReason is null;
    }

    private sealed record Offers(Offer? BuildBump, Offer? Jump);

    private async Task<Offers?> ComputeOffersAsync(Identity identity, CancellationToken cancellationToken)
    {
        var profiles = await _profileService.ListProfilesAsync(cancellationToken);
        var group = identity.Family == "bedrock" ? "bedrock-server" : identity.Family;

        var candidates = profiles
            .Where(p => p.Type.Equals("release", StringComparison.OrdinalIgnoreCase))
            .Where(p => p.Group.Equals(group, StringComparison.OrdinalIgnoreCase))
            .Select(p => ToOffer(p))
            .Where(o => o is not null)
            .ToList();

        return identity.Family switch
        {
            "paper" => JavaOffers(identity, candidates, hasBuilds: true),
            "velocity" => JavaOffers(identity, candidates, hasBuilds: true),
            "bungeecord" => BungeeOffers(identity, candidates),
            "vanilla" => JavaOffers(identity, candidates, hasBuilds: false),
            "bedrock" => new Offers(BedrockOffer(identity, candidates), null),
            _ => null
        };
    }

    /// <summary>BungeeCord has no Minecraft-version axis — only build numbers.</summary>
    private static Offers? BungeeOffers(Identity identity, List<Offer> candidates)
    {
        if (identity.Build is null)
        {
            return null;
        }

        Offer? bump = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Build is not null && candidate.Build > identity.Build &&
                (bump is null || candidate.Build > bump.Build))
            {
                bump = candidate;
            }
        }

        return new Offers(bump, null);
    }

    private static Offer? ToOffer(ProfileDto p)
    {
        if (p.Group.Equals("bedrock-server", StringComparison.OrdinalIgnoreCase) ||
            p.Group.Equals("vanilla", StringComparison.OrdinalIgnoreCase))
        {
            return new Offer(p.Id, p.Version, null);
        }

        // paper-1.21.11-140.jar / velocity-3.4.0-570.jar carry the build in the
        // filename; bungeecord encodes it as Version "build-2131".
        var buildMatch = Regex.Match(
            p.Filename ?? string.Empty, @"-(?<build>\d+)\.jar$", RegexOptions.IgnoreCase);
        if (!buildMatch.Success)
        {
            buildMatch = Regex.Match(p.Version, @"^(?:build-)?(?<build>\d+)$");
        }

        if (!buildMatch.Success)
        {
            return null;
        }

        var build = int.Parse(buildMatch.Groups["build"].Value);

        if (p.Group.Equals("bungeecord", StringComparison.OrdinalIgnoreCase))
        {
            return new Offer(p.Id, p.Version, build);
        }

        var mc = Regex.Match(p.Version, @"^\d+(?:\.\d+)*").Value;
        return mc.Length == 0 ? null : new Offer(p.Id, mc, build);
    }

    private static Offers? JavaOffers(Identity identity, List<Offer> candidates, bool hasBuilds)
    {
        if (identity.Version is null)
        {
            return null;
        }

        Offer? bump = null;
        Offer? jump = null;

        foreach (var candidate in candidates)
        {
            var comparison = CompareVersions(candidate.Version, identity.Version);
            if (comparison == 0)
            {
                if (hasBuilds && candidate.Build is not null &&
                    (identity.Build is null || candidate.Build > identity.Build) &&
                    (bump is null || candidate.Build > bump.Build))
                {
                    bump = candidate;
                }
            }
            else if (comparison > 0 && (jump is null || CompareVersions(candidate.Version, jump.Version) > 0))
            {
                jump = candidate;
            }
        }

        // Families without per-version builds (vanilla) have no bump offer — the
        // version jump IS the update and drives the badge. Families with builds
        // badge only on the safe same-version bump; jumps stay opt-in.
        return new Offers(hasBuilds ? bump : null, jump);
    }

    private static Offer? BedrockOffer(Identity identity, List<Offer> candidates)
    {
        Offer? latest = null;
        foreach (var candidate in candidates)
        {
            if (latest is null || CompareVersions(candidate.Version, latest.Version) > 0)
            {
                latest = candidate;
            }
        }

        // Unknown current version → no honest badge, even though the user could
        // still apply the latest manually.
        if (latest is null || identity.Version is null)
        {
            return null;
        }

        return CompareVersions(latest.Version, identity.Version) > 0 ? latest : null;
    }

    /// <summary>
    /// Dotted numeric comparison ("1.21.10" &lt; "1.21.11", "1.21.50.9" &lt;
    /// "1.21.50.10"). Missing segments count as zero.
    /// </summary>
    private static int CompareVersions(string left, string right)
    {
        var l = left.Split('.');
        var r = right.Split('.');
        for (var i = 0; i < Math.Max(l.Length, r.Length); i++)
        {
            var lv = i < l.Length && long.TryParse(l[i], out var lNum) ? lNum : 0;
            var rv = i < r.Length && long.TryParse(r[i], out var rNum) ? rNum : 0;
            if (lv != rv)
            {
                return lv.CompareTo(rv);
            }
        }

        return 0;
    }

    private async Task<Identity?> ResolveIdentityAsync(string name, string serverPath, CancellationToken cancellationToken)
    {
        // Bedrock first: its binary carries no parseable name, and the recorded
        // applied_version (written by ApplyUpdateAsync) is the only marker.
        var typeFile = Path.Combine(serverPath, ".mineos-server-type");
        var typeMarker = File.Exists(typeFile) ? (await File.ReadAllTextAsync(typeFile, cancellationToken)).Trim() : "";
        if (typeMarker.Equals("bedrock", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(serverPath, "bedrock_server")))
        {
            var settings = await ReadUpdatesSectionAsync(serverPath, cancellationToken);
            var applied = NullIfEmpty(settings.GetValueOrDefault("applied_version"))?
                .Split(':', 2)[0]; // tolerate legacy "version:build" keys
            return new Identity("bedrock", applied, null, null, null);
        }

        var sections = await ReadConfigSectionsAsync(serverPath, cancellationToken);
        var javaSection = sections.TryGetValue("java", out var java) ? java : new Dictionary<string, string>();
        var jarFile = NullIfEmpty(javaSection.GetValueOrDefault("jarfile")?.Trim());

        // Legacy servers often predate the jarfile config key — fall back to
        // scanning the directory for a recognized jar.
        if (jarFile is null)
        {
            jarFile = ScanForJar(serverPath);
        }

        if (jarFile is null)
        {
            return new Identity(null, null, null, null, null);
        }

        var fileName = Path.GetFileName(jarFile.TrimStart('@'));

        if (jarFile.StartsWith('@'))
        {
            return new Identity(null, null, null, fileName, "Modded servers using launcher argument files must be updated manually.");
        }

        foreach (var pattern in UnsupportedLoaderPatterns)
        {
            if (pattern.IsMatch(fileName))
            {
                return new Identity(null, null, null, fileName,
                    $"Modded servers ({fileName[..fileName.IndexOf('-')]}) must be updated manually — updating the loader can break installed mods.");
            }
        }

        foreach (var (family, pattern) in JarPatterns)
        {
            var match = pattern.Match(fileName);
            if (!match.Success)
            {
                continue;
            }

            return family switch
            {
                "paper" => new Identity(family, match.Groups["mc"].Value, int.Parse(match.Groups["build"].Value), fileName, null),
                "velocity" => new Identity(family, match.Groups["v"].Value,
                    match.Groups["build"].Success ? int.Parse(match.Groups["build"].Value) : null, fileName, null),
                "bungeecord" => new Identity(family, null, int.Parse(match.Groups["build"].Value), fileName, null),
                _ => new Identity(family, match.Groups["mc"].Value, null, fileName, null)
            };
        }

        return new Identity(null, null, null, fileName, null);
    }

    private static string? ScanForJar(string serverPath)
    {
        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(serverPath);
        }
        catch (Exception)
        {
            return null;
        }

        if (!dir.Exists)
        {
            return null;
        }

        FileInfo? best = null;
        foreach (var file in dir.EnumerateFiles("*.jar"))
        {
            if (file.Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!JarPatterns.Any(p => p.Pattern.IsMatch(file.Name)))
            {
                continue;
            }

            if (best is null || file.LastWriteTimeUtc > best.LastWriteTimeUtc)
            {
                best = file;
            }
        }

        return best?.Name;
    }

    // ------------------------------------------------------------------
    // Mode
    // ------------------------------------------------------------------

    public async Task SetUpdateModeAsync(string name, string mode, CancellationToken cancellationToken)
    {
        // Strict here: an unknown mode is caller error, not a legacy value to
        // quietly reinterpret (reads stay lenient via NormalizeMode).
        var normalized = (mode ?? "").Trim().ToLowerInvariant();
        if (normalized is not (ModeNotify or ModeOff or ModeIgnoreCurrent))
        {
            throw new ArgumentException(
                $"Unknown update mode '{mode}'. Use '{ModeNotify}', '{ModeIgnoreCurrent}', or '{ModeOff}'.");
        }

        var serverPath = GetServerPath(name);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        string? ignoredKey = null;
        if (normalized == ModeIgnoreCurrent)
        {
            // Dismissing requires knowing what to dismiss: compute the current
            // offer's key so the badge can return when something newer ships.
            var identity = await ResolveIdentityAsync(name, serverPath, cancellationToken);
            var offers = identity is not null && identity.Family is not null && identity.UnsupportedReason is null
                ? await ComputeOffersAsync(identity, cancellationToken)
                : null;
            var pending = offers?.BuildBump ?? offers?.Jump;
            if (pending is null)
            {
                throw new ArgumentException("There is no pending update to ignore.");
            }

            ignoredKey = OfferKey(pending);
        }

        var settings = await ReadUpdatesSectionAsync(serverPath, cancellationToken);
        settings["mode"] = normalized;
        if (ignoredKey is null)
        {
            settings.Remove("ignored_key");
        }
        else
        {
            settings["ignored_key"] = ignoredKey;
        }

        await WriteUpdatesSectionAsync(serverPath, settings, cancellationToken);
        _logger.LogInformation("Set update mode for {ServerName} to {Mode}", name, normalized);
    }

    private static string NormalizeMode(string? mode)
    {
        var trimmed = mode?.Trim().ToLowerInvariant() ?? "";
        return trimmed switch
        {
            ModeOff => ModeOff,
            ModeIgnoreCurrent => ModeIgnoreCurrent,
            _ => ModeNotify
        };
    }

    private static string OfferKey(Offer offer) =>
        offer.Build is null ? $"{offer.ProfileId}:{offer.Version}" : $"{offer.ProfileId}:{offer.Build}";

    // ------------------------------------------------------------------
    // Apply
    // ------------------------------------------------------------------

    public async Task<ApplyUpdateResultDto> ApplyUpdateAsync(string name, string profileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("A profile id is required.");
        }

        var serverPath = GetServerPath(name);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        var processInfo = _processManager.GetServerProcess(name);
        if (processInfo?.JavaPid is not null)
        {
            throw new InvalidOperationException($"Server '{name}' is running — stop it before applying an update.");
        }

        var identity = await ResolveIdentityAsync(name, serverPath, cancellationToken);
        if (identity is null || identity.Family is null || identity.UnsupportedReason is not null)
        {
            throw new ArgumentException(
                identity?.UnsupportedReason ?? "This server's software could not be identified, so it cannot be updated automatically.");
        }

        var profile = await _profileService.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
        {
            throw new ArgumentException($"Profile '{profileId}' not found.");
        }

        var profileFamily = profile.Group.Equals("bedrock-server", StringComparison.OrdinalIgnoreCase)
            ? "bedrock"
            : profile.Group.ToLowerInvariant();
        if (!profileFamily.Equals(identity.Family, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Profile '{profileId}' targets '{profileFamily}', but this server runs '{identity.Family}'.");
        }

        var downloaded = await _profileService.DownloadProfileAsync(profileId, cancellationToken);

        ApplyUpdateResultDto result = profileFamily == "bedrock"
            ? await ApplyBedrockAsync(name, serverPath, profile, downloaded, cancellationToken)
            : await ApplyJavaJarAsync(name, serverPath, identity, profile, downloaded, cancellationToken);

        await _serverService.MarkRestartRequiredAsync(name, cancellationToken);
        _logger.LogInformation("Applied update {ProfileId} to {ServerName}", profileId, name);
        return result;
    }

    private async Task<ApplyUpdateResultDto> ApplyJavaJarAsync(
        string name, string serverPath, Identity identity, ProfileDto profile, string downloaded, CancellationToken cancellationToken)
    {
        var targetPath = Path.Combine(serverPath, profile.Filename);
        File.Copy(downloaded, targetPath, overwrite: true);
        await OwnershipHelper.ChangeOwnershipAsync(targetPath, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);

        // Keep exactly one rollback generation: the previously configured jar,
        // renamed out of the way only after the new one is fully in place.
        var previous = identity.JarFileName;
        if (previous is not null && !previous.Equals(profile.Filename, StringComparison.OrdinalIgnoreCase))
        {
            BackupFile(Path.Combine(serverPath, previous));
        }

        var config = await _serverService.GetServerConfigAsync(name, cancellationToken);
        await _serverService.UpdateServerConfigAsync(
            name, config with { Java = config.Java with { JarFile = profile.Filename } }, cancellationToken);

        return new ApplyUpdateResultDto(profile.Id, previous, profile.Filename);
    }

    private async Task<ApplyUpdateResultDto> ApplyBedrockAsync(
        string name, string serverPath, ProfileDto profile, string downloaded, CancellationToken cancellationToken)
    {
        var binaryPath = Path.Combine(serverPath, "bedrock_server");
        BackupFile(binaryPath);

        ZipFile.ExtractToDirectory(downloaded, serverPath, overwriteFiles: true);

        try
        {
            var psi = new ProcessStartInfo("chmod", $"+x \"{binaryPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var chmod = Process.Start(psi);
            if (chmod is not null)
            {
                await chmod.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to chmod bedrock_server for {ServerName}", name);
        }

        OwnershipHelper.TrySetOwnership(serverPath, _options.RunAsUid, _options.RunAsGid, _logger, recursive: true);

        // Record what was installed so future detection works — bedrock binaries
        // carry no parseable version of their own.
        var settings = await ReadUpdatesSectionAsync(serverPath, cancellationToken);
        settings["applied_version"] = profile.Version;
        await WriteUpdatesSectionAsync(serverPath, settings, cancellationToken);

        return new ApplyUpdateResultDto(profile.Id, "bedrock_server", "bedrock_server");
    }

    private static void BackupFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backup = path + ".bak";
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }

        File.Move(path, backup);
    }

    // ------------------------------------------------------------------
    // [updates] section IO (owned here, carried through by the config writer)
    // ------------------------------------------------------------------

    private async Task<Dictionary<string, Dictionary<string, string>>> ReadConfigSectionsAsync(
        string serverPath, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(serverPath, "server.config");
        if (!File.Exists(configPath))
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken);
        return IniParser.ParseWithSections(content);
    }

    private async Task<Dictionary<string, string>> ReadUpdatesSectionAsync(string serverPath, CancellationToken cancellationToken)
    {
        var sections = await ReadConfigSectionsAsync(serverPath, cancellationToken);
        return sections.TryGetValue(UpdatesSection, out var section)
            ? new Dictionary<string, string>(section, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteUpdatesSectionAsync(
        string serverPath, Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        var sections = await ReadConfigSectionsAsync(serverPath, cancellationToken);
        sections[UpdatesSection] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        var configPath = Path.Combine(serverPath, "server.config");
        await File.WriteAllTextAsync(configPath, IniParser.WriteWithSections(sections), cancellationToken);
        OwnershipHelper.TrySetOwnership(configPath, _options.RunAsUid, _options.RunAsGid, _logger);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
