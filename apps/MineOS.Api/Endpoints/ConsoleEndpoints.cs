using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ConsoleEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapConsoleEndpoints(this RouteGroupBuilder servers)
    {
        // Send console command
        servers.MapPost("/{name}/console", async (
            string name,
            [FromBody] ConsoleCommandDto request,
            IConsoleService consoleService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await consoleService.SendCommandAsync(name, request.Command, cancellationToken);
                return Results.Ok(new { message = $"Command sent to server '{name}'" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // Stream console logs via SSE
        servers.MapGet("/{name}/console/stream", async (
            HttpContext context,
            string name,
            IConsoleService consoleService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            await foreach (var log in consoleService.StreamLogsAsync(name, cancellationToken))
            {
                var json = JsonSerializer.Serialize(log, JsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        });

        return servers;
    }
}

public record ConsoleCommandDto(string Command);
