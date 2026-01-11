using System.Text.Json;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class MonitoringEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapMonitoringEndpoints(this RouteGroupBuilder servers)
    {
        // Stream server heartbeat via SSE
        servers.MapGet("/{name}/heartbeat/stream", async (
            HttpContext context,
            string name,
            IMonitoringService monitoringService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            try
            {
                await foreach (var heartbeat in monitoringService.StreamHeartbeatAsync(name, cancellationToken))
                {
                    var json = JsonSerializer.Serialize(heartbeat, JsonOptions);
                    await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when client disconnects or request is cancelled.
            }
        });

        // Get ping info
        servers.MapGet("/{name}/ping", async (
            string name,
            IMonitoringService monitoringService,
            CancellationToken cancellationToken) =>
        {
            var ping = await monitoringService.GetPingInfoAsync(name, cancellationToken);
            return ping != null ? Results.Ok(ping) : Results.NotFound(new { error = "Server offline or unreachable" });
        });

        // Get query info
        servers.MapGet("/{name}/query", async (
            string name,
            IMonitoringService monitoringService,
            CancellationToken cancellationToken) =>
        {
            var query = await monitoringService.GetQueryInfoAsync(name, cancellationToken);
            return query != null ? Results.Ok(query) : Results.NotFound(new { error = "Query not enabled or server offline" });
        });

        // Get memory usage
        servers.MapGet("/{name}/memory", async (
            string name,
            IMonitoringService monitoringService,
            CancellationToken cancellationToken) =>
        {
            var memory = await monitoringService.GetMemoryInfoAsync(name, cancellationToken);
            return Results.Ok(memory);
        });

        return servers;
    }
}
