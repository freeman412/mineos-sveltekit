using Microsoft.AspNetCore.Mvc;
using MineOS.Api.Middleware;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class CurseForgeEndpoints
{
    public static RouteGroupBuilder MapCurseForgeEndpoints(this RouteGroupBuilder api)
    {
        var curseforge = api.MapGroup("/curseforge");

        curseforge.MapGet("/search", async (
            [FromQuery] string? query,
            [FromQuery] int? classId,
            [FromQuery] int? index,
            [FromQuery] int? pageSize,
            [FromQuery] string? sort,
            [FromQuery] string? order,
            [FromQuery] long? minDownloads,
            [FromQuery] string? gameVersion,
            ICurseForgeService curseForgeService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "Query is required" });
            }

            try
            {
                var results = await curseForgeService.SearchModsAsync(
                    query,
                    classId,
                    index ?? 0,
                    pageSize ?? 20,
                    sort,
                    order,
                    minDownloads,
                    gameVersion,
                    cancellationToken);
                return Results.Ok(results);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(502);
            }
        });

        curseforge.MapGet("/mod/{id:int}", async (
            int id,
            ICurseForgeService curseForgeService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var mod = await curseForgeService.GetModAsync(id, cancellationToken);
                return Results.Ok(mod);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(502);
            }
        });

        curseforge.MapGet("/mod/{id:int}/files", async (
            int id,
            [FromQuery] string? gameVersion,
            ICurseForgeService curseForgeService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var files = await curseForgeService.GetModFilesListAsync(id, gameVersion, cancellationToken);
                return Results.Ok(files);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(502);
            }
        });

        return api;
    }
}
