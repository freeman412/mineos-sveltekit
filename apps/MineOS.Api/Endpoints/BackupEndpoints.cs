using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class BackupEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapBackupEndpoints(this RouteGroupBuilder servers)
    {
        // Backup operations
        servers.MapGet("/{name}/backups", async (
            string name,
            IBackupService backupService,
            CancellationToken cancellationToken) =>
        {
            var backups = await backupService.ListBackupsAsync(name, cancellationToken);
            return Results.Ok(backups);
        });

        servers.MapPost("/{name}/backups", async (
            string name,
            IBackupService backupService,
            IBackgroundJobService jobService) =>
        {
            var jobId = jobService.QueueJob("backup", name, async (progress, ct) =>
            {
                await backupService.CreateBackupAsync(name, ct);
            });

            return Results.Accepted($"/api/v1/jobs/{jobId}", new { jobId, message = $"Backup queued for server '{name}'" });
        });

        servers.MapPost("/{name}/backups/restore", async (
            string name,
            [FromBody] RestoreBackupRequest request,
            IBackupService backupService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await backupService.RestoreBackupAsync(name, request.Timestamp, cancellationToken);
                return Results.Ok(new { message = $"Server '{name}' restored from backup" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        servers.MapDelete("/{name}/backups/prune", async (
            string name,
            [FromQuery] int keepCount,
            IBackupService backupService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await backupService.PruneBackupsAsync(name, keepCount, cancellationToken);
                return Results.Ok(new { message = $"Pruned backups for server '{name}'" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        servers.MapGet("/{name}/backups/sizes", async (
            string name,
            IBackupService backupService,
            CancellationToken cancellationToken) =>
        {
            var sizes = await backupService.GetBackupSizesAsync(name, cancellationToken);
            return Results.Ok(sizes);
        });

        return servers;
    }
}

public record RestoreBackupRequest(string Timestamp);
