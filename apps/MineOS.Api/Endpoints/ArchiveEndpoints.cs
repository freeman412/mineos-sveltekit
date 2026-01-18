using Microsoft.Extensions.DependencyInjection;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ArchiveEndpoints
{
    public static RouteGroupBuilder MapArchiveEndpoints(this RouteGroupBuilder servers)
    {
        // Archive operations
        servers.MapGet("/{name}/archives", async (
            string name,
            IArchiveService archiveService,
            CancellationToken cancellationToken) =>
        {
            var archives = await archiveService.ListArchivesAsync(name, cancellationToken);
            return Results.Ok(archives);
        });

        servers.MapPost("/{name}/archives", async (
            string name,
            IBackgroundJobService jobService) =>
        {
            var jobId = jobService.QueueJob("archive", name, async (services, progress, ct) =>
            {
                var archiveService = services.GetRequiredService<IArchiveService>();
                await archiveService.CreateArchiveAsync(name, ct);
            });

            return Results.Accepted($"/api/v1/jobs/{jobId}", new { jobId, message = $"Archive queued for server '{name}'" });
        });

        servers.MapDelete("/{name}/archives/{filename}", async (
            string name,
            string filename,
            IArchiveService archiveService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await archiveService.DeleteArchiveAsync(name, filename, cancellationToken);
                return Results.Ok(new { message = $"Archive '{filename}' deleted" });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        servers.MapGet("/{name}/archives/{filename}/download", async (
            string name,
            string filename,
            IArchiveService archiveService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var path = await archiveService.GetArchivePathAsync(name, filename, cancellationToken);
                return Results.File(path, "application/gzip", filename);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return servers;
    }
}
