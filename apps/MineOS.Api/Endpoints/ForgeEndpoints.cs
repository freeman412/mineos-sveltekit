using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ForgeEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };
    public static IEndpointRouteBuilder MapForgeEndpoints(this IEndpointRouteBuilder api)
    {
        var forge = api.MapGroup("/forge")
            .WithTags("Forge")
            .RequireAuthorization();

        forge.MapGet("/versions", async (
            IForgeService forgeService,
            CancellationToken cancellationToken) =>
        {
            var versions = await forgeService.GetVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetForgeVersions")
          .WithSummary("Get all available Forge versions");

        forge.MapGet("/versions/{minecraftVersion}", async (
            string minecraftVersion,
            IForgeService forgeService,
            CancellationToken cancellationToken) =>
        {
            var versions = await forgeService.GetVersionsForMinecraftAsync(minecraftVersion, cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetForgeVersionsForMinecraft")
          .WithSummary("Get Forge versions for a specific Minecraft version");

        forge.MapPost("/install", async (
            [FromBody] ForgeInstallRequest request,
            IForgeService forgeService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
            {
                return Results.BadRequest(new { error = "Minecraft version is required" });
            }
            if (string.IsNullOrWhiteSpace(request.ForgeVersion))
            {
                return Results.BadRequest(new { error = "Forge version is required" });
            }
            if (string.IsNullOrWhiteSpace(request.ServerName))
            {
                return Results.BadRequest(new { error = "Server name is required" });
            }

            var result = await forgeService.InstallForgeAsync(
                request.MinecraftVersion,
                request.ForgeVersion,
                request.ServerName,
                cancellationToken);

            if (result.Status == "failed")
            {
                return Results.BadRequest(new { error = result.Error });
            }

            return Results.Accepted($"/api/v1/forge/install/{result.InstallId}", new { data = result });
        }).WithName("InstallForge")
          .WithSummary("Start Forge installation for a server");

        forge.MapGet("/install/{installId}", async (
            string installId,
            IForgeService forgeService,
            CancellationToken cancellationToken) =>
        {
            var status = await forgeService.GetInstallStatusAsync(installId, cancellationToken);
            if (status == null)
            {
                return Results.NotFound(new { error = $"Installation '{installId}' not found" });
            }
            return Results.Ok(new { data = status });
        }).WithName("GetForgeInstallStatus")
          .WithSummary("Get Forge installation status");

        forge.MapGet("/install/{installId}/stream", async (
            HttpContext context,
            string installId,
            IForgeService forgeService) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var ct = context.RequestAborted;
            while (!ct.IsCancellationRequested)
            {
                var status = await forgeService.GetInstallStatusAsync(installId, ct);
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
        }).WithName("StreamForgeInstallStatus")
          .WithSummary("Stream Forge installation progress via SSE");

        return api;
    }
}

public record ForgeInstallRequest(string MinecraftVersion, string ForgeVersion, string ServerName);
