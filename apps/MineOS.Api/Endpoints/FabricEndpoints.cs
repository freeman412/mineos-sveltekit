using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class FabricEndpoints
{
    public static IEndpointRouteBuilder MapFabricEndpoints(this IEndpointRouteBuilder api)
    {
        var fabric = api.MapGroup("/fabric")
            .WithTags("Fabric")
            .RequireAuthorization();

        fabric.MapGet("/game-versions", async (
            IFabricService fabricService,
            CancellationToken cancellationToken) =>
        {
            var versions = await fabricService.GetGameVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetFabricGameVersions")
          .WithSummary("Get Minecraft versions supported by Fabric");

        fabric.MapGet("/loader-versions", async (
            IFabricService fabricService,
            CancellationToken cancellationToken) =>
        {
            var versions = await fabricService.GetLoaderVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetFabricLoaderVersions")
          .WithSummary("Get available Fabric loader versions");

        fabric.MapPost("/install", async (
            [FromBody] FabricInstallRequest request,
            IFabricService fabricService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
            {
                return Results.BadRequest(new { error = "Minecraft version is required" });
            }
            if (string.IsNullOrWhiteSpace(request.LoaderVersion))
            {
                return Results.BadRequest(new { error = "Loader version is required" });
            }
            if (string.IsNullOrWhiteSpace(request.ServerName))
            {
                return Results.BadRequest(new { error = "Server name is required" });
            }

            var result = await fabricService.InstallFabricAsync(
                request.MinecraftVersion,
                request.LoaderVersion,
                request.ServerName,
                cancellationToken);

            if (result.Status == "failed")
            {
                return Results.BadRequest(new { error = result.Error });
            }

            return Results.Accepted($"/api/v1/fabric/install/{result.InstallId}", new { data = result });
        }).WithName("InstallFabric")
          .WithSummary("Start Fabric installation for a server");

        fabric.MapGet("/install/{installId}", async (
            string installId,
            IFabricService fabricService,
            CancellationToken cancellationToken) =>
        {
            var status = await fabricService.GetInstallStatusAsync(installId, cancellationToken);
            if (status == null)
            {
                return Results.NotFound(new { error = $"Installation '{installId}' not found" });
            }
            return Results.Ok(new { data = status });
        }).WithName("GetFabricInstallStatus")
          .WithSummary("Get Fabric installation status");

        return api;
    }
}

public record FabricInstallRequest(string MinecraftVersion, string LoaderVersion, string ServerName);
