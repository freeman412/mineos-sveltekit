using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ServerEndpoints
{
    public static RouteGroupBuilder MapServerEndpoints(this RouteGroupBuilder api)
    {
        var servers = api.MapGroup("/servers")
            .RequireAuthorization()
            .WithMetadata(new Middleware.SkipApiKeyAttribute());

        // Server CRUD
        servers.MapPost("/", async (
            [FromBody] CreateServerRequest request,
            IServerService serverService,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // TODO: Get username from JWT claims
                var username = "admin";
                var server = await serverService.CreateServerAsync(request, username, cancellationToken);
                return Results.Created($"/api/servers/{server.Name}", server);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        servers.MapGet("/{name}", async (
            string name,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var server = await serverService.GetServerAsync(name, cancellationToken);
                return Results.Ok(server);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        servers.MapDelete("/{name}", async (
            string name,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await serverService.DeleteServerAsync(name, cancellationToken);
                return Results.NoContent();
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Server status
        servers.MapGet("/{name}/status", async (
            string name,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var heartbeat = await serverService.GetServerStatusAsync(name, cancellationToken);
                return Results.Ok(heartbeat);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        // Server actions
        servers.MapPost("/{name}/actions/{action}", async (
            string name,
            string action,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                switch (action.ToLower())
                {
                    case "start":
                        await serverService.StartServerAsync(name, cancellationToken);
                        return Results.Ok(new { message = $"Server '{name}' started" });

                    case "stop":
                        await serverService.StopServerAsync(name, 30, cancellationToken);
                        return Results.Ok(new { message = $"Server '{name}' stopped" });

                    case "restart":
                        await serverService.RestartServerAsync(name, cancellationToken);
                        return Results.Ok(new { message = $"Server '{name}' restarted" });

                    case "kill":
                        await serverService.KillServerAsync(name, cancellationToken);
                        return Results.Ok(new { message = $"Server '{name}' killed" });

                    default:
                        return Results.BadRequest(new { error = $"Unknown action: {action}" });
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (TimeoutException)
            {
                return Results.StatusCode(408); // Request Timeout
            }
        });

        // Server properties
        servers.MapGet("/{name}/server-properties", async (
            string name,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            var properties = await serverService.GetServerPropertiesAsync(name, cancellationToken);
            return Results.Ok(properties);
        });

        servers.MapPut("/{name}/server-properties", async (
            string name,
            [FromBody] Dictionary<string, string> properties,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await serverService.UpdateServerPropertiesAsync(name, properties, cancellationToken);
                return Results.Ok(new { message = "Properties updated" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Server config
        servers.MapGet("/{name}/server-config", async (
            string name,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            var config = await serverService.GetServerConfigAsync(name, cancellationToken);
            return Results.Ok(config);
        });

        servers.MapPut("/{name}/server-config", async (
            string name,
            [FromBody] ServerConfigDto config,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            await serverService.UpdateServerConfigAsync(name, config, cancellationToken);
            return Results.Ok(new { message = "Config updated" });
        });

        servers.MapPost("/{name}/eula", async (
            string name,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await serverService.AcceptEulaAsync(name, cancellationToken);
                return Results.Ok(new { message = $"EULA accepted for '{name}'" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        servers.MapPost("/{name}/ftb-install", async (
            string name,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await serverService.RunFtbInstallerAsync(name, cancellationToken);
                return Results.Ok(new { message = $"FTB installer completed for '{name}'" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Phase 2: Backup and archive endpoints
        servers.MapBackupEndpoints();
        servers.MapArchiveEndpoints();

        // Phase 3: Console and monitoring endpoints
        servers.MapConsoleEndpoints();
        servers.MapMonitoringEndpoints();

        // Phase 4: File management endpoints
        servers.MapFileEndpoints();

        // Phase 6: Mod management endpoints
        servers.MapModEndpoints();

        var cron = api.MapGroup("/servers/{name}/cron");
        cron.MapGet("/", (string name) => Results.Ok(Array.Empty<CronJobDto>()));
        cron.MapPost("/", (string name, CreateCronRequest _) =>
            EndpointHelpers.NotImplementedFeature($"cron.create:{name}"));
        cron.MapPatch("/{hash}", (string name, string hash, UpdateCronRequest _) =>
            EndpointHelpers.NotImplementedFeature($"cron.update:{name}:{hash}"));
        cron.MapDelete("/{hash}", (string name, string hash) =>
            EndpointHelpers.NotImplementedFeature($"cron.delete:{name}:{hash}"));

        var logs = api.MapGroup("/servers/{name}/logs");
        logs.MapGet("/", (string name) => Results.Ok(new { paths = Array.Empty<string>() }));
        logs.MapGet("/head/{*path}", (string name, string path) => Results.Ok(new { payload = "" }));

        return api;
    }
}
