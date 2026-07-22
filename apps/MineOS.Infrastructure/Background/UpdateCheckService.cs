using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Background;

/// <summary>
/// Hourly check comparing each server's installed server software against the
/// latest upstream release (Paper builds, Fabric/Quilt loader, Forge/NeoForge
/// for the same Minecraft line). Results are held in memory for
/// <see cref="IUpdateCheckService.GetUpdateInfo"/> (surfaced on
/// GET /host/servers), and one notification is created per server+version.
/// Detection never auto-updates and never flags when the installed version
/// can't be determined.
/// </summary>
public sealed class UpdateCheckService : BackgroundService, IUpdateCheckService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const string NotificationTitle = "Server Update Available";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRepository<SystemNotification> _notificationRepo;
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly ConcurrentDictionary<string, ServerUpdateInfo> _updates =
        new(StringComparer.OrdinalIgnoreCase);

    public UpdateCheckService(
        IServiceScopeFactory scopeFactory,
        IRepository<SystemNotification> notificationRepo,
        ILogger<UpdateCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _notificationRepo = notificationRepo;
        _logger = logger;
    }

    public ServerUpdateInfo? GetUpdateInfo(string serverName) =>
        _updates.TryGetValue(serverName, out var info) ? info : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllServersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Update check cycle failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckAllServersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var serverService = sp.GetRequiredService<IServerService>();

        var servers = await serverService.ListServersAsync(ct);
        foreach (var server in servers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var update = await CheckServerAsync(sp, serverService, server.Name, ct);
                if (update is null)
                {
                    _updates.TryRemove(server.Name, out _);
                }
                else
                {
                    _updates[server.Name] = update;
                    await MaybeNotifyAsync(update, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Update check failed for server {ServerName}", server.Name);
            }
        }

        // Drop entries for servers that no longer exist.
        var known = servers.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _updates.Keys.Where(k => !known.Contains(k)).ToList())
        {
            _updates.TryRemove(stale, out _);
        }
    }

    private static async Task<ServerUpdateInfo?> CheckServerAsync(
        IServiceProvider sp,
        IServerService serverService,
        string name,
        CancellationToken ct)
    {
        var loader = await serverService.DetectLoaderAsync(name, ct);
        var now = DateTimeOffset.UtcNow;

        switch (loader.Loader)
        {
            case "fabric" when loader.Version is not null:
            {
                var versions = await sp.GetRequiredService<IFabricService>().GetLoaderVersionsAsync(ct);
                var latest = UpdateDetection.PickLatest(versions.Where(v => v.IsStable).Select(v => v.Version));
                return NewerOrNull(name, "fabric loader", loader.Version, latest, now);
            }

            case "quilt" when loader.Version is not null:
            {
                var versions = await sp.GetRequiredService<IQuiltService>().GetLoaderVersionsAsync(ct);
                var latest = UpdateDetection.PickLatest(versions.Where(v => v.IsStable).Select(v => v.Version));
                return NewerOrNull(name, "quilt loader", loader.Version, latest, now);
            }

            case "neoforge" when loader.Version is not null:
            {
                var mcLine = UpdateDetection.NeoForgeMcLine(loader.Version);
                if (mcLine is null) return null;
                var versions = await sp.GetRequiredService<INeoForgeService>().GetVersionsAsync(ct);
                var latest = UpdateDetection.PickLatest(versions
                    .Select(v => v.NeoForgeVersion)
                    .Where(v => UpdateDetection.NeoForgeMcLine(v) == mcLine));
                return NewerOrNull(name, "neoforge", loader.Version, latest, now);
            }

            case "forge" when loader.Version is not null:
            {
                var split = UpdateDetection.TrySplitForgeVersion(loader.Version);
                if (split is not { } forge) return null;
                var versions = await sp.GetRequiredService<IForgeService>()
                    .GetVersionsForMinecraftAsync(forge.McVersion, ct);
                var latest = UpdateDetection.PickLatest(versions.Select(v => v.ForgeVersion));
                var update = NewerOrNull(name, "forge", forge.ForgeVersion, latest, now);
                return update;
            }

            case "paper":
            {
                var config = await serverService.GetServerConfigAsync(name, ct);
                var installed = UpdateDetection.TryParsePaperJar(config.Java.JarFile);
                if (installed is not { } paper) return null;

                var profiles = await sp.GetRequiredService<IProfileService>().ListProfilesAsync(ct);
                var latestJar = profiles.FirstOrDefault(p =>
                    p.Group == "paper" && p.Version == paper.McVersion)?.Filename;
                var latest = UpdateDetection.TryParsePaperJar(latestJar);
                if (latest is not { } latestPaper || latestPaper.Build <= paper.Build) return null;

                return new ServerUpdateInfo(
                    name, "paper",
                    $"{paper.McVersion} build {paper.Build}",
                    $"{latestPaper.McVersion} build {latestPaper.Build}",
                    now);
            }

            default:
                return null; // Unknown installed version — never guess.
        }
    }

    private static ServerUpdateInfo? NewerOrNull(
        string server, string component, string installed, string? latest, DateTimeOffset now)
    {
        if (latest is null || UpdateDetection.CompareVersions(latest, installed) <= 0) return null;
        return new ServerUpdateInfo(server, component, installed, latest, now);
    }

    private async Task MaybeNotifyAsync(ServerUpdateInfo update, CancellationToken ct)
    {
        var message =
            $"A newer {update.Component} is available for {update.ServerName}: " +
            $"{update.InstalledVersion} → {update.LatestVersion}.";

        // One active notification per server+latest-version: an undismissed
        // notification already naming this version means we've said our piece.
        var existing = await _notificationRepo.ToListAsync(
            n => n.ServerName == update.ServerName
                && n.Title == NotificationTitle
                && n.DismissedAt == null,
            ct);
        if (existing.Any(n => n.Message.Contains(update.LatestVersion, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await _notificationRepo.AddAsync(new SystemNotification
        {
            Type = "info",
            Title = NotificationTitle,
            Message = message,
            ServerName = update.ServerName,
            CreatedAt = DateTimeOffset.UtcNow,
            IsRead = false
        }, ct);
    }
}
