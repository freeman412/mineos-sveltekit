using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MineOS.Api.Authorization;
using MineOS.Application.Dtos;
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
        }).WithMetadata(new ServerAccessRequirement(ServerPermission.Console));

        // Clear console logs
        servers.MapDelete("/{name}/console", async (
            string name,
            [FromQuery] string? source,
            IConsoleService consoleService,
            CancellationToken cancellationToken) =>
        {
            var logSource = ParseLogSource(source);
            await consoleService.ClearLogsAsync(name, logSource, cancellationToken);
            return Results.NoContent();
        }).WithMetadata(new ServerAccessRequirement(ServerPermission.Console));

        // Stream console logs via SSE
        servers.MapGet("/{name}/console/stream", async (
            HttpContext context,
            string name,
            [FromQuery] string? source,
            IConsoleService consoleService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            try
            {
                var logSource = ParseLogSource(source);
                await foreach (var log in consoleService.StreamLogsAsync(name, logSource, cancellationToken))
                {
                    var json = JsonSerializer.Serialize(log, JsonOptions);
                    await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or request aborted.
            }
        }).WithMetadata(new ServerAccessRequirement(ServerPermission.Console));

        return servers;
    }

    private static ConsoleLogSource ParseLogSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ConsoleLogSource.Combined;
        }

        return source.Trim().ToLowerInvariant() switch
        {
            "java" => ConsoleLogSource.Java,
            "combined" => ConsoleLogSource.Combined,
            "all" => ConsoleLogSource.Combined,
            "crash" => ConsoleLogSource.CrashReports,
            "crash-reports" => ConsoleLogSource.CrashReports,
            "server" => ConsoleLogSource.Server,
            _ => ConsoleLogSource.Server
        };
    }
}

public record ConsoleCommandDto(string Command);
