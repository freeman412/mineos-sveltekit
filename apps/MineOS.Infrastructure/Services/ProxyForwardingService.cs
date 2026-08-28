using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Domain.ValueObjects;
using MineOS.Infrastructure.Utilities;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.RepresentationModel;

namespace MineOS.Infrastructure.Services;

/// <summary>
/// Works out whether a backend server behind a proxy is actually protected, and
/// configures it when it can be.
///
/// Nothing here is cached or persisted. Every answer is recomputed from the files
/// on disk, because those files are what the servers actually read, and users edit
/// them by hand.
/// </summary>
public class ProxyForwardingService : IProxyForwardingService
{
    private readonly IServerService _serverService;
    private readonly IModService _modService;
    private readonly IModrinthService _modrinthService;
    private readonly IProfileService _profileService;
    private readonly IContainerPortInspector _portInspector;
    private readonly HostOptions _options;
    private readonly ILogger<ProxyForwardingService> _logger;

    public ProxyForwardingService(
        IServerService serverService,
        IModService modService,
        IModrinthService modrinthService,
        IProfileService profileService,
        IContainerPortInspector portInspector,
        IOptions<HostOptions> options,
        ILogger<ProxyForwardingService> logger)
    {
        _serverService = serverService;
        _modService = modService;
        _modrinthService = modrinthService;
        _profileService = profileService;
        _portInspector = portInspector;
        _options = options.Value;
        _logger = logger;
    }

    // Mirrors ServerService's path layout, which is private to that class.
    private string GetServerPath(string name) =>
        Path.Combine(_options.BaseDirectory, _options.ServersPathSegment, name);

    private string GetPaperGlobalPath(string name) =>
        Path.Combine(GetServerPath(name), "config", "paper-global.yml");

    private string GetFabricProxyConfigPath(string name) =>
        Path.Combine(GetServerPath(name), "config", "FabricProxy-Lite.toml");

    public async Task<BackendForwardingDto> GetForwardingStatusAsync(
        string serverName, CancellationToken cancellationToken)
    {
        var servers = await _serverService.ListServersAsync(cancellationToken);
        return await BuildStatusAsync(serverName, servers, cancellationToken);
    }

    public async Task<ProxyBackendSummaryDto> GetProxyBackendsAsync(
        string proxyName, CancellationToken cancellationToken)
    {
        var servers = await _serverService.ListServersAsync(cancellationToken);
        var backendNames = await ResolveBackendNamesAsync(proxyName, servers, cancellationToken);

        var results = new List<BackendForwardingDto>();
        foreach (var backend in backendNames)
        {
            results.Add(await BuildStatusAsync(backend, servers, cancellationToken));
        }

        return new ProxyBackendSummaryDto(proxyName, results);
    }

    public async Task<BackendForwardingDto> SecureBackendAsync(
        string serverName, CancellationToken cancellationToken)
    {
        var servers = await _serverService.ListServersAsync(cancellationToken);
        var links = await FindClaimingProxiesAsync(serverName, servers, cancellationToken);

        if (links.Count == 0)
        {
            throw new InvalidOperationException(
                $"No proxy lists '{serverName}' as a backend. Add it to the proxy's servers list first.");
        }
        if (links.Count > 1)
        {
            // Refusing beats guessing: picking one would silently decide which
            // proxy is allowed to authenticate players for this server.
            throw new InvalidOperationException(
                $"'{serverName}' is claimed by more than one proxy ({string.Join(", ", links.Select(l => l.ProxyName))}). " +
                "Remove it from all but one before securing it.");
        }

        var link = links[0];
        if (link.Kind == ProxyForwardingKind.BungeeCord)
        {
            throw new InvalidOperationException(
                "BungeeCord's ip_forward carries no signature, so a backend behind it cannot verify " +
                "forwarded players. Use a Velocity proxy, or keep this server unreachable from outside.");
        }

        var loader = await SafeDetectLoaderAsync(serverName, cancellationToken);
        var tier = ProxyForwardingRules.TierFor(loader);
        if (tier == LoaderTier.Unsupported)
        {
            throw new InvalidOperationException(
                $"'{loader}' has no verified-forwarding support, so this backend cannot be secured. " +
                "Keep its port unreachable from outside instead.");
        }
        if (tier == LoaderTier.ModRequired && !await HasForwardingModAsync(serverName, cancellationToken))
        {
            throw new InvalidOperationException(
                $"This Fabric server needs the {FabricForwardingMod.DisplayName} mod before it can verify " +
                "forwarded players. Install it first — MineOS can do that for you.");
        }

        // 1. Ensure the proxy has a secret. Reuse an existing one — regenerating
        //    would break every sibling backend already secured against it.
        var secret = await EnsureForwardingSecretAsync(link.ProxyName, cancellationToken);

        // 2. Make the proxy verify forwarded players.
        await EnsureModernForwardingAsync(link.ProxyName, cancellationToken);

        // 3. Write the backend's half.
        if (tier == LoaderTier.Native)
        {
            await WritePaperVelocityBlockAsync(serverName, secret, cancellationToken);
        }
        else
        {
            await WriteFabricProxySecretAsync(serverName, secret, cancellationToken);
        }

        // 4. Only now hand authentication over to the proxy. If any step above
        //    threw, the server is still authenticating its own players: broken
        //    behind the proxy, but not open to impersonation.
        var properties = await _serverService.GetServerPropertiesAsync(serverName, cancellationToken);
        properties["online-mode"] = "false";
        await _serverService.UpdateServerPropertiesAsync(serverName, properties, cancellationToken);

        await _serverService.MarkRestartRequiredAsync(serverName, cancellationToken);
        await _serverService.MarkRestartRequiredAsync(link.ProxyName, cancellationToken);

        _logger.LogInformation(
            "Configured verified forwarding for backend {Backend} behind proxy {Proxy}",
            serverName, link.ProxyName);

        var refreshed = await _serverService.ListServersAsync(cancellationToken);
        return await BuildStatusAsync(serverName, refreshed, cancellationToken);
    }

    public async Task<BackendForwardingDto> InstallForwardingModAsync(
        string serverName, CancellationToken cancellationToken)
    {
        var loader = await SafeDetectLoaderAsync(serverName, cancellationToken);
        if (ProxyForwardingRules.TierFor(loader) != LoaderTier.ModRequired)
        {
            throw new InvalidOperationException(
                $"{FabricForwardingMod.DisplayName} is only for Fabric servers. " +
                $"'{loader ?? "This server"}' does not use it.");
        }

        if (await HasForwardingModAsync(serverName, cancellationToken))
        {
            _logger.LogInformation(
                "{Mod} already installed for {Server}; nothing to do", FabricForwardingMod.DisplayName, serverName);
            return await GetForwardingStatusAsync(serverName, cancellationToken);
        }

        var gameVersion = await ResolveGameVersionAsync(serverName, cancellationToken);
        var versions = await _modrinthService.GetProjectVersionsAsync(
            FabricForwardingMod.ModrinthProjectId, "fabric", gameVersion, cancellationToken);

        var version = FabricForwardingMod.SelectVersion(versions, gameVersion);
        var file = FabricForwardingMod.SelectFile(version);
        if (version is null || file is null)
        {
            // Refusing beats installing a build for the wrong Minecraft version:
            // that would leave a server that looks secured and verifies nothing.
            throw new InvalidOperationException(
                $"No {FabricForwardingMod.DisplayName} build was found for " +
                $"{(string.IsNullOrWhiteSpace(gameVersion) ? "this server" : $"Minecraft {gameVersion}")}. " +
                "Install it manually from the Mods tab, then secure this backend.");
        }

        // Hard dependencies first. Fabric refuses to boot at all when a mod's
        // required dependency is missing, so a half-installed set is worse than
        // no install: the button would leave the server unable to start.
        var installed = new List<string>();
        foreach (var dependencyId in FabricForwardingMod.RequiredDependencyProjects(version))
        {
            var dependencyVersions = await _modrinthService.GetProjectVersionsAsync(
                dependencyId, "fabric", gameVersion, cancellationToken);
            var dependencyVersion = FabricForwardingMod.SelectVersion(dependencyVersions, gameVersion);
            var dependencyFile = FabricForwardingMod.SelectFile(dependencyVersion);

            if (dependencyFile is null)
            {
                throw new InvalidOperationException(
                    $"{FabricForwardingMod.DisplayName} needs '{dependencyId}', but no build of it was found " +
                    $"for {(string.IsNullOrWhiteSpace(gameVersion) ? "this server" : $"Minecraft {gameVersion}")}. " +
                    "Nothing was installed.");
            }

            await InstallFileAsync(serverName, dependencyFile, cancellationToken);
            installed.Add(dependencyFile.FileName);
        }

        await InstallFileAsync(serverName, file, cancellationToken);
        installed.Add(file.FileName);

        await _serverService.MarkRestartRequiredAsync(serverName, cancellationToken);
        _logger.LogInformation(
            "Installed {Mod} {Version} for {Server}: {Files}",
            FabricForwardingMod.DisplayName, version.VersionNumber, serverName, string.Join(", ", installed));

        return await GetForwardingStatusAsync(serverName, cancellationToken);
    }

    private async Task InstallFileAsync(
        string serverName, ModrinthVersionFileDto file, CancellationToken cancellationToken)
    {
        await using var download = await _modrinthService.OpenDownloadStreamAsync(file.Url, cancellationToken);
        await _modService.SaveModAsync(serverName, file.FileName, download, cancellationToken);
    }

    private async Task<bool> HasForwardingModAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            var mods = await _modService.ListModsAsync(serverName, cancellationToken);
            // A disabled jar loads nothing, so it does not count as installed.
            return mods.Any(m => !m.IsDisabled && FabricForwardingMod.IsForwardingModJar(m.FileName));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list mods for {Server}", serverName);
            return false;
        }
    }

    /// <summary>
    /// The Minecraft version this server runs, used to pick a matching mod build.
    /// Mirrors how the mods endpoints resolve it: detection first, then the
    /// configured profile as a fallback.
    /// </summary>
    private async Task<string?> ResolveGameVersionAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            // DetectLoaderAsync now reports the game version separately from the
            // loader version, so this uses the same source as the mods browser
            // rather than parsing jar names a second time.
            var detected = await _serverService.DetectLoaderAsync(serverName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(detected.MinecraftVersion))
            {
                return detected.MinecraftVersion;
            }

            var config = await _serverService.GetServerConfigAsync(serverName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(config.Minecraft.Profile))
            {
                var profile = await _profileService.GetProfileAsync(config.Minecraft.Profile, cancellationToken);
                if (!string.IsNullOrWhiteSpace(profile?.Version))
                {
                    return profile!.Version;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve the Minecraft version for {Server}", serverName);
        }

        return null;
    }

    // ---- status assembly -------------------------------------------------

    private async Task<BackendForwardingDto> BuildStatusAsync(
        string serverName, List<ServerDetailDto> servers, CancellationToken cancellationToken)
    {
        var links = await FindClaimingProxiesAsync(serverName, servers, cancellationToken);
        var link = links.FirstOrDefault();

        var loader = await SafeDetectLoaderAsync(serverName, cancellationToken);
        var tier = ProxyForwardingRules.TierFor(loader);
        var onlineMode = await ReadOnlineModeAsync(serverName, cancellationToken);

        var configured = false;
        var secretMatches = false;

        if (link is not null && link.Kind != ProxyForwardingKind.BungeeCord)
        {
            var proxySecret = await ReadForwardingSecretAsync(link.ProxyName, cancellationToken);
            var backendSecret = tier switch
            {
                LoaderTier.Native => ReadPaperVelocitySecret(serverName, out configured),
                LoaderTier.ModRequired => ReadFabricProxySecret(serverName, out configured),
                _ => null
            };
            secretMatches = SecretsAgree(proxySecret, backendSecret);
        }

        var facts = new ForwardingFacts(
            IsBackend: link is not null,
            ProxyKind: link?.Kind ?? ProxyForwardingKind.None,
            Tier: tier,
            BackendForwardingConfigured: configured,
            SecretMatches: secretMatches,
            ServerOnlineMode: onlineMode);

        var assessment = ProxyForwardingRules.Resolve(facts);

        var hasForwardingMod = tier == LoaderTier.ModRequired &&
                               await HasForwardingModAsync(serverName, cancellationToken);

        // The exposure check is the only control left when nothing can be
        // verified, so that is exactly when we spend the call.
        var exposure = ExposureVerdict.Unknown;
        string? exposureDetail = null;
        if (assessment.Status == ProxyForwardingStatus.Unverifiable || assessment.IsSpoofable)
        {
            (exposure, exposureDetail) = await CheckExposureAsync(serverName, cancellationToken);
        }

        var remediation = assessment.Status switch
        {
            ProxyForwardingStatus.Securable or ProxyForwardingStatus.Misconfigured when tier == LoaderTier.Native
                => "secure",
            ProxyForwardingStatus.Securable or ProxyForwardingStatus.Misconfigured when tier == LoaderTier.ModRequired
                => hasForwardingMod ? "secure" : "install-mod",
            _ => null
        };

        return new BackendForwardingDto(
            ServerName: serverName,
            Status: assessment.Status.ToString(),
            IsSpoofable: assessment.IsSpoofable,
            ProxyKind: facts.ProxyKind.ToString(),
            Tier: tier.ToString(),
            ProxyName: link?.ProxyName,
            Loader: loader,
            ServerOnlineMode: onlineMode,
            BackendForwardingConfigured: configured,
            SecretMatches: secretMatches,
            Exposure: exposure.ToString(),
            ExposureDetail: exposureDetail,
            RemediationAction: remediation);
    }

    private sealed record ProxyLink(string ProxyName, ProxyForwardingKind Kind);

    /// <summary>
    /// Finds every proxy whose backend list points at this server. Matching is by
    /// listen port plus a local-looking host: all servers share a host here, so the
    /// port is what actually identifies the target.
    /// </summary>
    private async Task<List<ProxyLink>> FindClaimingProxiesAsync(
        string serverName, List<ServerDetailDto> servers, CancellationToken cancellationToken)
    {
        var endpoint = await _serverService.GetServerListenEndpointAsync(serverName, cancellationToken);
        var links = new List<ProxyLink>();
        if (endpoint is null)
        {
            return links;
        }

        foreach (var proxy in servers.Where(s =>
                     string.Equals(s.ServerType, "proxy", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase)))
        {
            var addresses = await ReadBackendAddressesAsync(proxy.Name, cancellationToken);
            if (addresses.Kind == ProxyForwardingKind.None)
            {
                continue;
            }
            if (addresses.Addresses.Any(a => AddressTargets(a, endpoint.Value.Port)))
            {
                links.Add(new ProxyLink(proxy.Name, addresses.Kind));
            }
        }

        return links;
    }

    private async Task<List<string>> ResolveBackendNamesAsync(
        string proxyName, List<ServerDetailDto> servers, CancellationToken cancellationToken)
    {
        var addresses = await ReadBackendAddressesAsync(proxyName, cancellationToken);
        var names = new List<string>();

        foreach (var server in servers.Where(s =>
                     !string.Equals(s.ServerType, "proxy", StringComparison.OrdinalIgnoreCase)))
        {
            var endpoint = await _serverService.GetServerListenEndpointAsync(server.Name, cancellationToken);
            if (endpoint is null)
            {
                continue;
            }
            if (addresses.Addresses.Any(a => AddressTargets(a, endpoint.Value.Port)))
            {
                names.Add(server.Name);
            }
        }

        return names;
    }

    private sealed record BackendAddresses(ProxyForwardingKind Kind, IReadOnlyList<string> Addresses);

    private async Task<BackendAddresses> ReadBackendAddressesAsync(
        string proxyName, CancellationToken cancellationToken)
    {
        try
        {
            var velocity = await _serverService.GetVelocityConfigAsync(proxyName, cancellationToken);
            if (velocity.Exists)
            {
                var kind = string.Equals(velocity.PlayerInfoForwardingMode, "modern", StringComparison.OrdinalIgnoreCase)
                    ? ProxyForwardingKind.VelocityModern
                    : ProxyForwardingKind.VelocityUnverified;
                return new BackendAddresses(kind, velocity.Servers.Values.ToList());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read velocity.toml for {Proxy}", proxyName);
        }

        try
        {
            var bungee = await _serverService.GetBungeeConfigAsync(proxyName, cancellationToken);
            if (bungee.Exists)
            {
                var addresses = (bungee.Servers ?? new Dictionary<string, BungeeBackendDto>())
                    .Values.Where(b => b is not null).Select(b => b.Address).ToList();
                return new BackendAddresses(ProxyForwardingKind.BungeeCord, addresses);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read config.yml for {Proxy}", proxyName);
        }

        return new BackendAddresses(ProxyForwardingKind.None, Array.Empty<string>());
    }

    /// <summary>
    /// True when a proxy backend address points at the given local port. Hosts are
    /// compared loosely because "localhost", "127.0.0.1" and the container name all
    /// resolve to the same server in practice; the port is the discriminator.
    /// </summary>
    internal static bool AddressTargets(string address, int port)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }
        var lastColon = address.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == address.Length - 1)
        {
            return false;
        }
        return int.TryParse(address[(lastColon + 1)..], out var parsed) && parsed == port;
    }

    // ---- reads -----------------------------------------------------------

    private async Task<string?> SafeDetectLoaderAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            var loader = await _serverService.DetectLoaderAsync(serverName, cancellationToken);
            return loader.Loader;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect loader for {Server}", serverName);
            return null;
        }
    }

    private async Task<bool> ReadOnlineModeAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await _serverService.GetServerPropertiesAsync(serverName, cancellationToken);
            // Minecraft's own default is true, and so is ours: assuming the safe
            // value when the file is missing avoids inventing a scary warning.
            return !properties.TryGetValue("online-mode", out var value) ||
                   !string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read server.properties for {Server}", serverName);
            return true;
        }
    }

    private string? ReadPaperVelocitySecret(string serverName, out bool configured)
    {
        configured = false;
        var path = GetPaperGlobalPath(serverName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(File.ReadAllText(path));
            stream.Load(reader);
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return null;
            }

            if (!TryGetMap(root, "proxies", out var proxies) ||
                !TryGetMap(proxies!, "velocity", out var velocity))
            {
                return null;
            }

            configured = ReadYamlBool(velocity!, "enabled");
            return ReadYamlString(velocity!, "secret");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read paper-global.yml for {Server}", serverName);
            return null;
        }
    }

    private string? ReadFabricProxySecret(string serverName, out bool configured)
    {
        configured = false;
        var path = GetFabricProxyConfigPath(serverName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var model = Toml.ToModel(File.ReadAllText(path));
            var secret = model.TryGetValue("secret", out var value) ? value as string : null;
            configured = !string.IsNullOrEmpty(secret);
            return secret;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read FabricProxy-Lite.toml for {Server}", serverName);
            return null;
        }
    }

    private async Task<string?> ReadForwardingSecretAsync(string proxyName, CancellationToken cancellationToken)
    {
        var path = await GetForwardingSecretPathAsync(proxyName, cancellationToken);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read forwarding secret for {Proxy}", proxyName);
            return null;
        }
    }

    private async Task<string> GetForwardingSecretPathAsync(string proxyName, CancellationToken cancellationToken)
    {
        var fileName = "forwarding.secret";
        try
        {
            var velocity = await _serverService.GetVelocityConfigAsync(proxyName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(velocity.ForwardingSecretFile))
            {
                fileName = velocity.ForwardingSecretFile;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read velocity.toml for {Proxy}; assuming forwarding.secret", proxyName);
        }

        // Velocity resolves this relative to its own directory. Reject anything
        // that tries to climb out of it.
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "forwarding.secret";
        }
        return Path.Combine(GetServerPath(proxyName), safeName);
    }

    /// <summary>
    /// Length-independent comparison of the two sides' secrets. Both must be
    /// present: two missing secrets are not a match, they are two holes.
    /// </summary>
    internal static bool SecretsAgree(string? proxySecret, string? backendSecret)
    {
        if (string.IsNullOrWhiteSpace(proxySecret) || string.IsNullOrWhiteSpace(backendSecret))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(proxySecret.Trim()),
            System.Text.Encoding.UTF8.GetBytes(backendSecret.Trim()));
    }

    // ---- writes ----------------------------------------------------------

    private async Task<string> EnsureForwardingSecretAsync(string proxyName, CancellationToken cancellationToken)
    {
        var existing = await ReadForwardingSecretAsync(proxyName, cancellationToken);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var path = await GetForwardingSecretPathAsync(proxyName, cancellationToken);
        await File.WriteAllTextAsync(path, secret, cancellationToken);
        TryRestrictPermissions(path);
        await OwnershipHelper.ChangeOwnershipAsync(path, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);

        _logger.LogInformation("Generated a forwarding secret for proxy {Proxy}", proxyName);
        return secret;
    }

    private void TryRestrictPermissions(string path)
    {
        try
        {
            // Key material: owner read/write only.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restrict permissions on {Path}", path);
        }
    }

    private async Task EnsureModernForwardingAsync(string proxyName, CancellationToken cancellationToken)
    {
        var velocity = await _serverService.GetVelocityConfigAsync(proxyName, cancellationToken);
        if (string.Equals(velocity.PlayerInfoForwardingMode, "modern", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _serverService.UpdateVelocityConfigAsync(
            proxyName,
            velocity with { PlayerInfoForwardingMode = "modern" },
            cancellationToken);

        _logger.LogInformation("Switched proxy {Proxy} to modern player-info forwarding", proxyName);
    }

    /// <summary>
    /// <summary>
    /// Creates a directory the server process must be able to write to, and makes sure it
    /// is owned by the account the server runs as.
    /// </summary>
    /// <remarks>
    /// Chowning only the file we write is not enough. This service runs as root inside the
    /// API container while servers run as the unprivileged owner uid, so a directory it
    /// creates is left root-owned and the server cannot create anything in it. Paper writes
    /// paper-global.yml and paper-world-defaults.yml into config/ during startup: it fails
    /// with AccessDeniedException, then dies on the missing world-defaults file it was
    /// prevented from writing.
    ///
    /// This only bit brand-new servers secured before their first start, since a config/
    /// left over from an earlier run already exists and is already owned correctly - which
    /// is why it looked like an odd one-off rather than every proxy backend.
    ///
    /// The chown is unconditional rather than only-when-created, so an install already
    /// holding a root-owned config/ repairs itself the next time forwarding is written.
    /// </remarks>
    private async Task EnsureOwnedDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await OwnershipHelper.ChangeOwnershipAsync(
            directory, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);
    }

    /// <summary>
    /// Writes proxies.velocity into paper-global.yml, editing the existing tree in
    /// place. Paper's file carries well over a hundred keys we do not model, and
    /// regenerating it would silently discard every one of them.
    /// </summary>
    private async Task WritePaperVelocityBlockAsync(
        string serverName, string secret, CancellationToken cancellationToken)
    {
        var path = GetPaperGlobalPath(serverName);
        await EnsureOwnedDirectoryAsync(Path.GetDirectoryName(path)!, cancellationToken);

        var existingYaml = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : null;

        await File.WriteAllTextAsync(path, UpsertPaperVelocityBlock(existingYaml, secret), cancellationToken);
        TryRestrictPermissions(path);
        await OwnershipHelper.ChangeOwnershipAsync(path, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);
    }

    /// <summary>
    /// Returns paper-global.yml with proxies.velocity set for verified forwarding,
    /// editing the supplied document in place rather than regenerating it.
    ///
    /// Paper's file carries well over a hundred keys MineOS does not model, and a
    /// regenerated file would silently discard every one of them — so this is kept
    /// pure and covered by a round-trip test.
    /// </summary>
    internal static string UpsertPaperVelocityBlock(string? existingYaml, string secret)
    {
        var root = new YamlMappingNode();
        if (!string.IsNullOrWhiteSpace(existingYaml))
        {
            var stream = new YamlStream();
            using var reader = new StringReader(existingYaml);
            stream.Load(reader);
            if (stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode existing)
            {
                root = existing;
            }
        }

        var proxies = GetOrCreateMap(root, "proxies");
        var velocity = GetOrCreateMap(proxies, "velocity");
        velocity.Children[new YamlScalarNode("enabled")] = new YamlScalarNode("true");
        // Paper's online-mode under velocity means "let the proxy tell us who this
        // player is, and trust it because the payload is signed".
        velocity.Children[new YamlScalarNode("online-mode")] = new YamlScalarNode("true");
        velocity.Children[new YamlScalarNode("secret")] = new YamlScalarNode(secret);

        var writer = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private async Task WriteFabricProxySecretAsync(
        string serverName, string secret, CancellationToken cancellationToken)
    {
        var path = GetFabricProxyConfigPath(serverName);
        var model = File.Exists(path)
            ? Toml.ToModel(await File.ReadAllTextAsync(path, cancellationToken))
            : new TomlTable();

        model["secret"] = secret;
        if (!model.ContainsKey("hackOnlineMode"))
        {
            model["hackOnlineMode"] = true;
        }

        await EnsureOwnedDirectoryAsync(Path.GetDirectoryName(path)!, cancellationToken);
        await File.WriteAllTextAsync(path, Toml.FromModel(model), cancellationToken);
        TryRestrictPermissions(path);
        await OwnershipHelper.ChangeOwnershipAsync(path, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);
    }

    // ---- exposure --------------------------------------------------------

    private async Task<(ExposureVerdict, string?)> CheckExposureAsync(
        string serverName, CancellationToken cancellationToken)
    {
        var endpoint = await _serverService.GetServerListenEndpointAsync(serverName, cancellationToken);
        if (endpoint is null)
        {
            return (ExposureVerdict.Unknown, "Could not determine which port this server listens on.");
        }
        return await _portInspector.IsPortPublishedAsync(endpoint.Value.Port, cancellationToken);
    }

    // ---- yaml helpers ----------------------------------------------------

    private static bool TryGetMap(YamlMappingNode parent, string key, out YamlMappingNode? map)
    {
        map = null;
        if (parent.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlMappingNode found)
        {
            map = found;
            return true;
        }
        return false;
    }

    private static YamlMappingNode GetOrCreateMap(YamlMappingNode parent, string key)
    {
        if (TryGetMap(parent, key, out var existing))
        {
            return existing!;
        }
        var created = new YamlMappingNode();
        parent.Children[new YamlScalarNode(key)] = created;
        return created;
    }

    private static string? ReadYamlString(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s
            ? s.Value
            : null;

    private static bool ReadYamlBool(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var v) &&
        v is YamlScalarNode s &&
        bool.TryParse(s.Value, out var parsed) && parsed;
}
