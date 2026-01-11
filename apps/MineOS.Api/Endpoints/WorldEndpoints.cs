using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class WorldEndpoints
{
    public static IEndpointRouteBuilder MapWorldEndpoints(this IEndpointRouteBuilder api)
    {
        var worlds = api.MapGroup("/api/servers/{serverName}/worlds")
            .WithTags("Worlds")
            .RequireAuthorization();

        worlds.MapGet("/", async (
            string serverName,
            IWorldService worldService,
            CancellationToken cancellationToken) =>
        {
            var result = await worldService.ListWorldsAsync(serverName, cancellationToken);
            return Results.Ok(new { data = result });
        }).WithName("ListWorlds")
          .WithSummary("List all worlds for a server");

        worlds.MapGet("/{worldName}", async (
            string serverName,
            string worldName,
            IWorldService worldService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await worldService.GetWorldInfoAsync(serverName, worldName, cancellationToken);
                return Results.Ok(new { data = result });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("GetWorldInfo")
          .WithSummary("Get detailed information about a world");

        worlds.MapGet("/{worldName}/download", async (
            string serverName,
            string worldName,
            IWorldService worldService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var stream = await worldService.DownloadWorldAsync(serverName, worldName, cancellationToken);
                return Results.File(stream, "application/zip", $"{serverName}-{worldName}.zip");
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("DownloadWorld")
          .WithSummary("Download a world as a ZIP archive");

        worlds.MapPost("/{worldName}/upload", async (
            string serverName,
            string worldName,
            [FromForm] IFormFile file,
            IWorldService worldService,
            CancellationToken cancellationToken) =>
        {
            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No file uploaded" });
            }

            if (!file.ContentType.Equals("application/zip", StringComparison.OrdinalIgnoreCase) &&
                !file.ContentType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "File must be a ZIP archive" });
            }

            try
            {
                using var stream = file.OpenReadStream();
                await worldService.UploadWorldAsync(serverName, worldName, stream, cancellationToken);
                return Results.Ok(new { message = $"World '{worldName}' uploaded successfully" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("UploadWorld")
          .WithSummary("Upload and replace a world from a ZIP archive")
          .DisableAntiforgery();

        worlds.MapDelete("/{worldName}", async (
            string serverName,
            string worldName,
            IWorldService worldService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await worldService.DeleteWorldAsync(serverName, worldName, cancellationToken);
                return Results.Ok(new { message = $"World '{worldName}' deleted successfully" });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("DeleteWorld")
          .WithSummary("Delete/reset a world");

        return api;
    }
}
