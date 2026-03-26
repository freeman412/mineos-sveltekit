using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class NeoForgeEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapNeoForgeEndpoints(this IEndpointRouteBuilder api)
    {
        var neoforge = api.MapGroup("/neoforge")
            .WithTags("NeoForge")
            .RequireAuthorization();

        neoforge.MapGet("/versions", async (
            INeoForgeService neoForgeService,
            CancellationToken cancellationToken) =>
        {
            var versions = await neoForgeService.GetVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetNeoForgeVersions")
          .WithSummary("Get all NeoForge versions");

        neoforge.MapGet("/versions/{minecraftVersion}", async (
            string minecraftVersion,
            INeoForgeService neoForgeService,
            CancellationToken cancellationToken) =>
        {
            var versions = await neoForgeService.GetVersionsForMinecraftAsync(minecraftVersion, cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetNeoForgeVersionsForMinecraft")
          .WithSummary("Get NeoForge versions for a specific Minecraft version");

        neoforge.MapPost("/install", async (
            [FromBody] NeoForgeInstallRequest request,
            INeoForgeService neoForgeService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
                return Results.BadRequest(new { error = "Minecraft version is required" });
            if (string.IsNullOrWhiteSpace(request.NeoForgeVersion))
                return Results.BadRequest(new { error = "NeoForge version is required" });
            if (string.IsNullOrWhiteSpace(request.ServerName))
                return Results.BadRequest(new { error = "Server name is required" });

            var result = await neoForgeService.InstallNeoForgeAsync(
                request.MinecraftVersion, request.NeoForgeVersion,
                request.ServerName, cancellationToken);

            if (result.Status == "failed")
                return Results.BadRequest(new { error = result.Error });

            return Results.Accepted($"/api/v1/neoforge/install/{result.InstallId}", new { data = result });
        }).WithName("InstallNeoForge")
          .WithSummary("Start NeoForge installation for a server");

        neoforge.MapGet("/install/{installId}", async (
            string installId,
            INeoForgeService neoForgeService,
            CancellationToken cancellationToken) =>
        {
            var status = await neoForgeService.GetInstallStatusAsync(installId, cancellationToken);
            if (status == null)
                return Results.NotFound(new { error = $"Installation '{installId}' not found" });
            return Results.Ok(new { data = status });
        }).WithName("GetNeoForgeInstallStatus")
          .WithSummary("Get NeoForge installation status");

        neoforge.MapGet("/install/{installId}/stream", async (
            HttpContext context,
            string installId,
            INeoForgeService neoForgeService) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var ct = context.RequestAborted;
            while (!ct.IsCancellationRequested)
            {
                var status = await neoForgeService.GetInstallStatusAsync(installId, ct);
                if (status == null)
                {
                    await context.Response.WriteAsync($"data: {{\"status\":\"completed\",\"progress\":100}}\n\n", ct);
                    break;
                }

                var json = System.Text.Json.JsonSerializer.Serialize(status, CamelCaseJsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);

                if (status.Status is "completed" or "failed")
                    break;

                await Task.Delay(1000, ct);
            }
        }).WithName("StreamNeoForgeInstallStatus")
          .WithSummary("Stream NeoForge installation progress via SSE");

        return api;
    }
}

public record NeoForgeInstallRequest(string MinecraftVersion, string NeoForgeVersion, string ServerName);
