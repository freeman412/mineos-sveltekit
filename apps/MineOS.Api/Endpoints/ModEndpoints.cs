using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ModEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapModEndpoints(this RouteGroupBuilder servers)
    {
        servers.MapGet("/{name}/mods", async (
            string name,
            IModService modService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var mods = await modService.ListModsAsync(name, cancellationToken);
                return Results.Ok(mods);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        servers.MapPost("/{name}/mods/upload", async (
            string name,
            HttpRequest request,
            IModService modService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (request.HasFormContentType)
                {
                    var form = await request.ReadFormAsync(cancellationToken);
                    var file = form.Files.FirstOrDefault();
                    if (file == null)
                    {
                        return Results.BadRequest(new { error = "Mod file is required" });
                    }

                    await using var stream = file.OpenReadStream();
                    await modService.SaveModAsync(name, file.FileName, stream, cancellationToken);
                    return Results.Ok(new { message = $"Uploaded mod '{file.FileName}'" });
                }

                var fileName = request.Query["filename"].ToString();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return Results.BadRequest(new { error = "Missing filename query parameter" });
                }

                await using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                await modService.SaveModAsync(name, fileName, buffer, cancellationToken);
                return Results.Ok(new { message = $"Uploaded mod '{fileName}'" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        servers.MapDelete("/{name}/mods/{filename}", async (
            string name,
            string filename,
            IModService modService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await modService.DeleteModAsync(name, filename, cancellationToken);
                return Results.Ok(new { message = $"Deleted mod '{filename}'" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
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

        servers.MapGet("/{name}/mods/{filename}/download", async (
            string name,
            string filename,
            IModService modService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var path = await modService.GetModPathAsync(name, filename, cancellationToken);
                return Results.File(path, "application/java-archive", filename);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
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

        servers.MapPost("/{name}/mods/install-from-curseforge", async (
            string name,
            [FromBody] InstallModRequest request,
            IModService modService,
            IBackgroundJobService jobService) =>
        {
            var jobId = jobService.QueueJob("mod-install", name, async (progress, ct) =>
            {
                await modService.InstallModFromCurseForgeAsync(name, request.ModId, request.FileId, progress, ct);
            });

            return Results.Accepted($"/api/v1/jobs/{jobId}", new { jobId, message = "Mod install queued" });
        });

        servers.MapPost("/{name}/modpacks/install", async (
            string name,
            [FromBody] InstallModpackRequest request,
            IModService modService,
            IBackgroundJobService jobService) =>
        {
            var jobId = jobService.QueueJob("modpack-install", name, async (progress, ct) =>
            {
                await modService.InstallModpackAsync(name, request.ModpackId, request.FileId, progress, ct);
            });

            return Results.Accepted($"/api/v1/jobs/{jobId}", new { jobId, message = "Modpack install queued" });
        });

        // Enhanced modpack install with state tracking and rollback
        servers.MapPost("/{name}/modpacks/install-enhanced", async (
            string name,
            [FromBody] InstallModpackEnhancedRequest request,
            IModService modService,
            IBackgroundJobService jobService) =>
        {
            var jobId = jobService.QueueModpackInstall(name, async (state, ct) =>
            {
                await modService.InstallModpackWithStateAsync(
                    name,
                    request.ModpackId,
                    request.FileId,
                    request.ModpackName,
                    request.ModpackVersion,
                    request.LogoUrl,
                    state,
                    ct);
            });

            return Results.Accepted($"/api/v1/servers/{name}/modpacks/install/{jobId}/stream", new { jobId, message = "Modpack install queued" });
        });

        // Stream modpack install progress with output
        servers.MapGet("/{name}/modpacks/install/{jobId}/stream", async (
            HttpContext context,
            string name,
            string jobId,
            IBackgroundJobService jobService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            await foreach (var progress in jobService.StreamModpackProgressAsync(jobId, cancellationToken))
            {
                var json = JsonSerializer.Serialize(progress, JsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }

            return Results.Empty;
        });

        // List installed modpacks
        servers.MapGet("/{name}/modpacks", async (
            string name,
            IModService modService,
            ILogger<IModService> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var modpacks = await modService.ListInstalledModpacksAsync(name, cancellationToken);
                return Results.Ok(modpacks);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to list modpacks for server {ServerName}", name);
                return Results.Problem(ex.Message);
            }
        });

        // Uninstall modpack
        servers.MapDelete("/{name}/modpacks/{modpackId:int}", async (
            string name,
            int modpackId,
            IModService modService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await modService.UninstallModpackAsync(name, modpackId, cancellationToken);
                return Results.Ok(new { message = "Modpack uninstalled" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        // List mods with modpack associations
        servers.MapGet("/{name}/mods/with-modpacks", async (
            string name,
            IModService modService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var mods = await modService.ListModsWithModpacksAsync(name, cancellationToken);
                return Results.Ok(mods);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        servers.MapGet("/{name}/mods/stream", async (
            HttpContext context,
            string name,
            [FromQuery] string? jobId,
            IBackgroundJobService jobService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Results.BadRequest(new { error = "jobId query parameter is required" });
            }

            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            await foreach (var progress in jobService.StreamJobProgressAsync(jobId, cancellationToken))
            {
                var json = JsonSerializer.Serialize(progress, JsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }

            return Results.Empty;
        });

        return servers;
    }
}

public record InstallModRequest(int ModId, int? FileId);

public record InstallModpackRequest(int ModpackId, int? FileId);

public record InstallModpackEnhancedRequest(
    int ModpackId,
    int? FileId,
    string ModpackName,
    string? ModpackVersion,
    string? LogoUrl);
