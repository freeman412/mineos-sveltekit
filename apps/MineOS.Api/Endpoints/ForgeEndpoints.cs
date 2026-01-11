using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ForgeEndpoints
{
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

        return api;
    }
}

public record ForgeInstallRequest(string MinecraftVersion, string ForgeVersion, string ServerName);
