using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Dtos;

namespace MineOS.Api.Endpoints;

public static class ServerEndpoints
{
    public static RouteGroupBuilder MapServerEndpoints(this RouteGroupBuilder api)
    {
        var servers = api.MapGroup("/servers");

        servers.MapPost("/", (CreateServerRequest _) =>
            EndpointHelpers.NotImplementedFeature("servers.create"));

        servers.MapDelete("/{name}", ([FromRoute] string name, [FromBody] DeleteServerRequest _) =>
            EndpointHelpers.NotImplementedFeature($"servers.delete:{name}"));

        servers.MapGet("/{name}/status", (string name) =>
        {
            var heartbeat = new ServerHeartbeatDto(
                Up: false,
                Memory: null,
                Ping: null,
                Query: null,
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            return Results.Ok(heartbeat);
        });

        servers.MapPost("/{name}/actions/{action}", (string name, string action, ActionRequest _) =>
            EndpointHelpers.NotImplementedFeature($"servers.action:{name}:{action}"));

        servers.MapPost("/{name}/console", (string name, ConsoleCommandDto _) =>
            EndpointHelpers.NotImplementedFeature($"servers.console:{name}"));

        servers.MapGet("/{name}/server-properties", (string name) =>
            Results.Ok(new Dictionary<string, string>()));

        servers.MapPut("/{name}/server-properties", (string name, Dictionary<string, string> _) =>
            EndpointHelpers.NotImplementedFeature($"servers.server-properties.put:{name}"));

        servers.MapGet("/{name}/server-config", (string name) =>
            Results.Ok(new Dictionary<string, Dictionary<string, string>>()));

        servers.MapPut("/{name}/server-config", (string name, Dictionary<string, Dictionary<string, string>> _) =>
            EndpointHelpers.NotImplementedFeature($"servers.server-config.put:{name}"));

        servers.MapGet("/{name}/archives", (string name) =>
            Results.Ok(Array.Empty<ArchiveEntryDto>()));

        servers.MapGet("/{name}/backups", (string name) =>
            Results.Ok(Array.Empty<IncrementEntryDto>()));

        servers.MapGet("/{name}/backups/sizes", (string name) =>
            Results.Ok(Array.Empty<IncrementEntryDto>()));

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
