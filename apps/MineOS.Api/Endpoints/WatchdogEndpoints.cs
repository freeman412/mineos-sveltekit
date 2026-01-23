using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class WatchdogEndpoints
{
    public static RouteGroupBuilder MapWatchdogEndpoints(this RouteGroupBuilder servers)
    {
        // Get crash events for a specific server
        servers.MapGet("/{name}/crashes", async (
            string name,
            IWatchdogService watchdogService,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var events = await watchdogService.GetCrashEventsAsync(name, limit ?? 20, cancellationToken);
            return Results.Ok(events);
        });

        // Clear crash history for a server
        servers.MapDelete("/{name}/crashes", async (
            string name,
            IWatchdogService watchdogService,
            CancellationToken cancellationToken) =>
        {
            await watchdogService.ClearCrashHistoryAsync(name, cancellationToken);
            return Results.NoContent();
        });

        // Get watchdog status for a specific server
        servers.MapGet("/{name}/watchdog", (
            string name,
            IWatchdogService watchdogService) =>
        {
            var status = watchdogService.GetWatchdogStatus();
            if (status.TryGetValue(name, out var serverStatus))
            {
                return Results.Ok(serverStatus);
            }
            return Results.Ok(new
            {
                serverName = name,
                isMonitoring = false,
                wasRunning = false,
                restartAttempts = 0,
                lastCrashTime = (DateTimeOffset?)null,
                lastRestartAttempt = (DateTimeOffset?)null,
                cooldownEndsAt = (DateTimeOffset?)null
            });
        });

        return servers;
    }

    public static WebApplication MapGlobalWatchdogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/watchdog")
            .RequireAuthorization();

        // Get all crash events across all servers
        group.MapGet("/crashes", async (
            IWatchdogService watchdogService,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var events = await watchdogService.GetAllCrashEventsAsync(limit ?? 50, cancellationToken);
            return Results.Ok(events);
        });

        // Get watchdog status for all servers
        group.MapGet("/status", (IWatchdogService watchdogService) =>
        {
            var status = watchdogService.GetWatchdogStatus();
            return Results.Ok(status);
        });

        return app;
    }
}
