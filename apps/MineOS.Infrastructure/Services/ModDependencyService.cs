using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

/// <summary>
/// Resolves what a single-mod install really involves.
///
/// Modpack installs have resolved dependencies for a while
/// (<c>ModService.ResolveModrinthDependenciesAsync</c>), but installing one mod
/// from the browser wrote exactly one file. A mod whose hard dependency is missing
/// does not warn on startup — Fabric aborts the boot entirely — so the failure
/// landed on the user as "my server stopped working after I installed a mod".
/// </summary>
public class ModDependencyService : IModDependencyService
{
    // A mod graph deep enough to hit this is a sign something is wrong; the visited
    // set already prevents cycles, so this only bounds pathological breadth.
    private const int MaxResolutionRounds = 64;

    private readonly IModrinthService _modrinthService;
    private readonly IModService _modService;
    private readonly IServerService _serverService;
    private readonly ILogger<ModDependencyService> _logger;

    public ModDependencyService(
        IModrinthService modrinthService,
        IModService modService,
        IServerService serverService,
        ILogger<ModDependencyService> logger)
    {
        _modrinthService = modrinthService;
        _modService = modService;
        _serverService = serverService;
        _logger = logger;
    }

    public async Task<ModInstallPlanDto> PlanInstallAsync(
        string serverName, string versionId, CancellationToken cancellationToken)
    {
        var root = await _modrinthService.GetVersionAsync(versionId, cancellationToken)
                   ?? throw new InvalidOperationException($"Modrinth version '{versionId}' was not found.");

        var rootFile = SelectPrimaryFile(root)
                       ?? throw new InvalidOperationException("That version has no downloadable file.");

        var (loader, gameVersion) = await ResolveServerTargetAsync(serverName, cancellationToken);
        var installedProjectIds = await GetInstalledProjectIdsAsync(serverName, cancellationToken);

        var required = new List<ModDependencyItemDto>();
        var optional = new List<ModDependencyItemDto>();
        var alreadyInstalled = new List<ModDependencyItemDto>();
        var problems = new List<string>();

        // Breadth-first over hard dependencies. `seen` is what stops a dependency
        // cycle from looping forever, and also keeps a diamond dependency from
        // being installed twice.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<ModrinthDependencyDto>();

        foreach (var dependency in root.Dependencies ?? Array.Empty<ModrinthDependencyDto>())
        {
            EnqueueOrOffer(dependency, queue, seen, optional);
        }

        var rounds = 0;
        while (queue.Count > 0 && rounds++ < MaxResolutionRounds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dependency = queue.Dequeue();
            var projectId = dependency.ProjectId!;

            if (installedProjectIds.TryGetValue(projectId, out var installedName))
            {
                alreadyInstalled.Add(new ModDependencyItemDto(
                    projectId, null, installedName, null, "required"));
                continue;
            }

            var resolved = await ResolveDependencyVersionAsync(
                dependency, loader, gameVersion, cancellationToken);

            if (resolved is null)
            {
                var label = await DescribeProjectAsync(projectId, cancellationToken);
                problems.Add(
                    $"{label} has no build for {DescribeTarget(loader, gameVersion)}");
                continue;
            }

            var file = SelectPrimaryFile(resolved);
            if (file is null)
            {
                problems.Add($"{projectId} has no downloadable file");
                continue;
            }

            required.Add(new ModDependencyItemDto(
                resolved.ProjectId,
                resolved.Id,
                await DescribeProjectAsync(resolved.ProjectId, cancellationToken),
                file.FileName,
                "required"));

            // Dependencies of dependencies.
            foreach (var child in resolved.Dependencies ?? Array.Empty<ModrinthDependencyDto>())
            {
                EnqueueOrOffer(child, queue, seen, optional);
            }
        }

        // Optional entries are collected during the walk, where only the project id
        // is known. They are shown to a human who has to decide whether to install
        // them, and "EE4vxUHj" is not a decision anyone can make — so resolve the
        // titles before returning. Only optional ones need this; required entries
        // already carry a name from their resolved version.
        for (var i = 0; i < optional.Count; i++)
        {
            var name = await DescribeProjectAsync(optional[i].ProjectId, cancellationToken);
            optional[i] = optional[i] with { Name = name };
        }

        return new ModInstallPlanDto(
            VersionId: root.Id,
            ProjectId: root.ProjectId,
            Name: root.Name,
            FileName: rootFile.FileName,
            Required: required,
            Optional: optional,
            AlreadyInstalled: alreadyInstalled,
            Problems: problems);
    }

    public async Task<ModInstallResultDto> InstallWithDependenciesAsync(
        string serverName,
        string versionId,
        IReadOnlyList<string> approvedOptionalProjectIds,
        CancellationToken cancellationToken)
    {
        var plan = await PlanInstallAsync(serverName, versionId, cancellationToken);

        if (plan.Problems.Count > 0)
        {
            // All-or-nothing on purpose: a partially installed set is the exact
            // state that stops a server booting, and it is harder to diagnose than
            // a refusal with a reason.
            throw new InvalidOperationException(
                $"Cannot install '{plan.Name}': {string.Join("; ", plan.Problems)}. Nothing was installed.");
        }

        var (loader, gameVersion) = await ResolveServerTargetAsync(serverName, cancellationToken);
        var installed = new List<string>();

        // Dependencies first, so the mod is never on disk without them — even if
        // something fails midway.
        foreach (var dependency in plan.Required)
        {
            await InstallVersionAsync(serverName, dependency.VersionId!, cancellationToken);
            installed.Add(dependency.FileName!);
        }

        foreach (var projectId in approvedOptionalProjectIds ?? Array.Empty<string>())
        {
            var offered = plan.Optional.FirstOrDefault(o =>
                string.Equals(o.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
            if (offered is null)
            {
                // Only things the plan actually offered may be installed: this is
                // what stops an arbitrary project id being smuggled in.
                _logger.LogWarning(
                    "Ignoring optional dependency {ProjectId}: it was not part of the plan for {Server}",
                    projectId, serverName);
                continue;
            }

            var resolved = await ResolveDependencyVersionAsync(
                new ModrinthDependencyDto(offered.ProjectId, offered.VersionId, "optional"),
                loader, gameVersion, cancellationToken);
            var file = SelectPrimaryFile(resolved);
            if (resolved is null || file is null)
            {
                _logger.LogWarning(
                    "Skipping optional dependency {ProjectId}: no compatible build", projectId);
                continue;
            }

            await InstallVersionAsync(serverName, resolved.Id, cancellationToken);
            installed.Add(file.FileName);
        }

        await InstallVersionAsync(serverName, plan.VersionId, cancellationToken);
        installed.Add(plan.FileName);

        await _serverService.MarkRestartRequiredAsync(serverName, cancellationToken);
        _logger.LogInformation(
            "Installed {Count} file(s) for {Server}: {Files}",
            installed.Count, serverName, string.Join(", ", installed));

        return new ModInstallResultDto(
            installed,
            plan.AlreadyInstalled.Select(a => a.Name).ToList());
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Queues a hard dependency for resolution, or records an optional one to
    /// offer. Dependencies with no project id (Modrinth allows a bare version
    /// reference) cannot be resolved or de-duplicated, so they are skipped.
    /// </summary>
    private static void EnqueueOrOffer(
        ModrinthDependencyDto dependency,
        Queue<ModrinthDependencyDto> queue,
        HashSet<string> seen,
        List<ModDependencyItemDto> optional)
    {
        if (string.IsNullOrWhiteSpace(dependency.ProjectId) || !seen.Add(dependency.ProjectId))
        {
            return;
        }

        if (string.Equals(dependency.DependencyType, "required", StringComparison.OrdinalIgnoreCase))
        {
            queue.Enqueue(dependency);
        }
        else if (string.Equals(dependency.DependencyType, "optional", StringComparison.OrdinalIgnoreCase))
        {
            optional.Add(new ModDependencyItemDto(
                dependency.ProjectId, dependency.VersionId, dependency.ProjectId, null, "optional"));
        }
        // "incompatible" and "embedded" are deliberately ignored: neither is
        // something to install alongside the mod.
    }

    private async Task<ModrinthVersionDto?> ResolveDependencyVersionAsync(
        ModrinthDependencyDto dependency,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(dependency.VersionId))
        {
            var pinned = await _modrinthService.GetVersionAsync(dependency.VersionId, cancellationToken);
            if (pinned is not null && IsCompatible(pinned, loader, gameVersion))
            {
                return pinned;
            }
        }

        if (string.IsNullOrWhiteSpace(dependency.ProjectId))
        {
            return null;
        }

        var candidates = await _modrinthService.GetProjectVersionsAsync(
            dependency.ProjectId, loader, gameVersion, cancellationToken);

        return candidates?
            .Where(v => IsCompatible(v, loader, gameVersion))
            .OrderByDescending(v => v.DatePublished)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether a version suits this server. An empty loader or game-version list
    /// on Modrinth's side means "unspecified", which is treated as compatible
    /// rather than rejected — refusing those would block datapack-style projects
    /// that legitimately declare neither.
    /// </summary>
    internal static bool IsCompatible(ModrinthVersionDto version, string? loader, string? gameVersion)
    {
        if (!string.IsNullOrWhiteSpace(gameVersion) &&
            version.GameVersions is { Count: > 0 } &&
            !version.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(loader) &&
            version.Loaders is { Count: > 0 } &&
            !version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    internal static ModrinthVersionFileDto? SelectPrimaryFile(ModrinthVersionDto? version)
    {
        if (version?.Files is null || version.Files.Count == 0)
        {
            return null;
        }
        return version.Files.FirstOrDefault(f => f.Primary)
               ?? version.Files.FirstOrDefault(f => f.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
    }

    internal static string DescribeTarget(string? loader, string? gameVersion) =>
        (loader, gameVersion) switch
        {
            (null or "", null or "") => "this server",
            (null or "", _) => $"Minecraft {gameVersion}",
            (_, null or "") => loader!,
            _ => $"{loader} {gameVersion}"
        };

    private async Task<string> DescribeProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        try
        {
            var project = await _modrinthService.GetProjectAsync(projectId, cancellationToken);
            return string.IsNullOrWhiteSpace(project?.Title) ? projectId : project!.Title;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Modrinth project {ProjectId}", projectId);
            return projectId;
        }
    }

    private async Task InstallVersionAsync(
        string serverName, string versionId, CancellationToken cancellationToken)
    {
        var version = await _modrinthService.GetVersionAsync(versionId, cancellationToken)
                      ?? throw new InvalidOperationException($"Modrinth version '{versionId}' disappeared mid-install.");
        var file = SelectPrimaryFile(version)
                   ?? throw new InvalidOperationException($"Modrinth version '{versionId}' has no downloadable file.");

        await using var stream = await _modrinthService.OpenDownloadStreamAsync(file.Url, cancellationToken);
        await _modService.SaveModAsync(serverName, file.FileName, stream, cancellationToken);
    }

    private async Task<(string? Loader, string? GameVersion)> ResolveServerTargetAsync(
        string serverName, CancellationToken cancellationToken)
    {
        try
        {
            var detected = await _serverService.DetectLoaderAsync(serverName, cancellationToken);
            return (detected.Loader, detected.MinecraftVersion);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect the loader for {Server}", serverName);
            return (null, null);
        }
    }

    /// <summary>
    /// Which Modrinth projects this server already has, by hashing the installed
    /// jars and asking Modrinth in a single call. File names are not usable for
    /// this — they say nothing reliable about which project a jar is.
    /// </summary>
    private async Task<Dictionary<string, string>> GetInstalledProjectIdsAsync(
        string serverName, CancellationToken cancellationToken)
    {
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var mods = await _modService.ListModsAsync(serverName, cancellationToken);
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mod in mods.Where(m => !m.IsDisabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = await _modService.GetModPathAsync(serverName, mod.FileName, cancellationToken);
                if (!File.Exists(path))
                {
                    continue;
                }

                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                hashes[hash] = mod.FileName;
            }

            if (hashes.Count == 0)
            {
                return installed;
            }

            var found = await _modrinthService.GetVersionsByFileHashesAsync(
                hashes.Keys.ToList(), "sha1", cancellationToken);

            foreach (var (hash, version) in found)
            {
                installed[version.ProjectId] = hashes.TryGetValue(hash, out var fileName)
                    ? fileName
                    : version.Name;
            }
        }
        catch (Exception ex)
        {
            // Worst case we offer to install something already present, which
            // overwrites the same file. That is far better than failing the plan.
            _logger.LogWarning(ex, "Could not determine installed mods for {Server}", serverName);
        }

        return installed;
    }
}
