using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class QuiltEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapQuiltEndpoints(this IEndpointRouteBuilder api)
    {
        var quilt = api.MapGroup("/quilt")
            .WithTags("Quilt")
            .RequireAuthorization();

        quilt.MapGet("/game-versions", async (
            IQuiltService quiltService,
            CancellationToken cancellationToken) =>
        {
            var versions = await quiltService.GetGameVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetQuiltGameVersions")
          .WithSummary("Get Minecraft versions supported by Quilt");

        quilt.MapGet("/loader-versions", async (
            IQuiltService quiltService,
            CancellationToken cancellationToken) =>
        {
            var versions = await quiltService.GetLoaderVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetQuiltLoaderVersions")
          .WithSummary("Get available Quilt loader versions");

        quilt.MapPost("/install", async (
            [FromBody] QuiltInstallRequest request,
            IQuiltService quiltService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
                return Results.BadRequest(new { error = "Minecraft version is required" });
            if (string.IsNullOrWhiteSpace(request.LoaderVersion))
                return Results.BadRequest(new { error = "Loader version is required" });
            if (string.IsNullOrWhiteSpace(request.ServerName))
                return Results.BadRequest(new { error = "Server name is required" });

            var result = await quiltService.InstallQuiltAsync(
                request.MinecraftVersion, request.LoaderVersion,
                request.ServerName, cancellationToken);

            if (result.Status == "failed")
                return Results.BadRequest(new { error = result.Error });

            return Results.Accepted($"/api/v1/quilt/install/{result.InstallId}", new { data = result });
        }).WithName("InstallQuilt")
          .WithSummary("Start Quilt installation for a server");

        quilt.MapGet("/install/{installId}", async (
            string installId,
            IQuiltService quiltService,
            CancellationToken cancellationToken) =>
        {
            var status = await quiltService.GetInstallStatusAsync(installId, cancellationToken);
            if (status == null)
                return Results.NotFound(new { error = $"Installation '{installId}' not found" });
            return Results.Ok(new { data = status });
        }).WithName("GetQuiltInstallStatus")
          .WithSummary("Get Quilt installation status");

        quilt.MapGet("/install/{installId}/stream", async (
            HttpContext context,
            string installId,
            IQuiltService quiltService) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var ct = context.RequestAborted;
            while (!ct.IsCancellationRequested)
            {
                var status = await quiltService.GetInstallStatusAsync(installId, ct);
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
        }).WithName("StreamQuiltInstallStatus")
          .WithSummary("Stream Quilt installation progress via SSE");

        return api;
    }
}

public record QuiltInstallRequest(string MinecraftVersion, string LoaderVersion, string ServerName);
