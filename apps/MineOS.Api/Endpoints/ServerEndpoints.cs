using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MineOS.Api.Authorization;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ServerEndpoints
{
    public static RouteGroupBuilder MapServerEndpoints(this RouteGroupBuilder api)
    {
        var servers = api.MapGroup("/servers");
        servers.AddEndpointFilter<ServerAccessFilter>();

        // Server CRUD
        servers.MapPost("/", async (
            [FromBody] CreateServerRequest request,
            IServerService serverService,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!IsAdminOrApiKey(context))
                {
                    return Results.Forbid();
                }

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

        servers.MapPost("/{name}/clone", async (
            string name,
            [FromBody] CloneServerRequest request,
            IServerService serverService,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!IsAdminOrApiKey(context))
                {
                    return Results.Forbid();
                }

                if (string.IsNullOrWhiteSpace(request.NewName))
                {
                    return Results.BadRequest(new { error = "New server name is required." });
                }

                var server = await serverService.CloneServerAsync(name, request.NewName.Trim(), cancellationToken);
                return Results.Ok(server);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        servers.MapGet("/list", async (
            IServerService serverService,
            IServerAccessService serverAccessService,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var serverList = await serverService.ListServersAsync(cancellationToken);
            if (user?.Identity?.IsAuthenticated != true)
            {
                return Results.Ok(serverList);
            }

            var role = user.FindFirstValue(ClaimTypes.Role) ?? "user";
            if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(serverList);
            }

            if (!TryGetUserId(user, out var userId))
            {
                return Results.Unauthorized();
            }

            var allowedServerNames = await serverAccessService.ListServerNamesAsync(userId, cancellationToken);
            var allowedSet = new HashSet<string>(allowedServerNames, StringComparer.OrdinalIgnoreCase);
            var filtered = serverList.Where(server => allowedSet.Contains(server.Name)).ToList();
            return Results.Ok(filtered);
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
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!IsAdminOrApiKey(context))
                {
                    return Results.Forbid();
                }

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

        // Server icon upload
        servers.MapPost("/{name}/icon", async (
            string name,
            HttpRequest request,
            IFileService fileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer, cancellationToken);
                var imageData = buffer.ToArray();

                // Validate it's a PNG file (check PNG header: 89 50 4E 47)
                if (imageData.Length < 8 ||
                    imageData[0] != 0x89 || imageData[1] != 0x50 ||
                    imageData[2] != 0x4E || imageData[3] != 0x47)
                {
                    return Results.BadRequest(new { error = "File must be a PNG image" });
                }

                // Save as server-icon.png in the server directory
                await fileService.WriteFileBytesAsync(name, "/server-icon.png", imageData, cancellationToken);
                return Results.Ok(new { message = "Server icon uploaded successfully" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        servers.MapGet("/{name}/icon", async (
            string name,
            IFileService fileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var iconData = await fileService.ReadFileBytesAsync(name, "/server-icon.png", cancellationToken);
                return Results.File(iconData, "image/png");
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "Server icon not found" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        servers.MapDelete("/{name}/icon", async (
            string name,
            IFileService fileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await fileService.DeleteFileAsync(name, "/server-icon.png", cancellationToken);
                return Results.Ok(new { message = "Server icon deleted successfully" });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "Server icon not found" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        // Phase 2: Backup and archive endpoints
        servers.MapBackupEndpoints();
        servers.MapArchiveEndpoints();

        // Phase 2b: Client package endpoints
        servers.MapClientPackageEndpoints();

        // Phase 3: Console and monitoring endpoints
        servers.MapConsoleEndpoints();
        servers.MapMonitoringEndpoints();
        servers.MapWatchdogEndpoints();

        // Phase 4: File management endpoints
        servers.MapFileEndpoints();

        // Phase 6: Mod management endpoints
        servers.MapModEndpoints();

        // Phase 6b: Plugin management endpoints
        servers.MapPluginEndpoints();

        var cron = api.MapGroup("/servers/{name}/cron");
        cron.AddEndpointFilter<ServerAccessFilter>();
        cron.MapGet("/", (string name) => Results.Ok(Array.Empty<CronJobDto>()));
        cron.MapPost("/", (string name, CreateCronRequest _) =>
            EndpointHelpers.NotImplementedFeature($"cron.create:{name}"));
        cron.MapPatch("/{hash}", (string name, string hash, UpdateCronRequest _) =>
            EndpointHelpers.NotImplementedFeature($"cron.update:{name}:{hash}"));
        cron.MapDelete("/{hash}", (string name, string hash) =>
            EndpointHelpers.NotImplementedFeature($"cron.delete:{name}:{hash}"));

        var logs = api.MapGroup("/servers/{name}/logs");
        logs.AddEndpointFilter<ServerAccessFilter>();
        logs.MapGet("/", (string name) => Results.Ok(new { paths = Array.Empty<string>() }));
        logs.MapGet("/head/{*path}", (string name, string path) => Results.Ok(new { payload = "" }));

        return api;
    }

    private static bool IsAdminOrApiKey(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        var role = context.User.FindFirstValue(ClaimTypes.Role) ?? "user";
        return string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out int userId)
    {
        userId = 0;
        var claim = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? user.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        return int.TryParse(claim, out userId);
    }
}
