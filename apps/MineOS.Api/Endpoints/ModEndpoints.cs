using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
            IBackgroundJobService jobService) =>
        {
            var jobId = jobService.QueueJob("mod-install", name, async (services, progress, ct) =>
            {
                var modService = services.GetRequiredService<IModService>();
                await modService.InstallModFromCurseForgeAsync(name, request.ModId, request.FileId, progress, ct);
            });

            return Results.Accepted($"/api/v1/jobs/{jobId}", new { jobId, message = "Mod install queued" });
        });

        servers.MapPost("/{name}/modpacks/install", async (
            string name,
            [FromBody] InstallModpackRequest request,
            IBackgroundJobService jobService) =>
        {
            var jobId = jobService.QueueJob("modpack-install", name, async (services, progress, ct) =>
            {
                var modService = services.GetRequiredService<IModService>();
                await modService.InstallModpackAsync(name, request.ModpackId, request.FileId, progress, ct);
            });

            return Results.Accepted($"/api/v1/jobs/{jobId}", new { jobId, message = "Modpack install queued" });
        });

        // Enhanced modpack install with state tracking and rollback
        servers.MapPost("/{name}/modpacks/install-enhanced", async (
            string name,
            [FromBody] InstallModpackEnhancedRequest request,
            IBackgroundJobService jobService) =>
        {
            var jobId = jobService.QueueModpackInstall(name, async (services, state, ct) =>
            {
                var modService = services.GetRequiredService<IModService>();
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

        var modrinth = servers.MapGroup("/{name}/mods/modrinth");

        modrinth.MapGet("/search", async (
            string name,
            [FromQuery] string query,
            [FromQuery] int? index,
            [FromQuery] int? pageSize,
            [FromQuery] string? loader,
            [FromQuery] string? gameVersion,
            IModrinthService modrinthService,
            IServerService serverService,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                return Results.Ok(new { index = 0, pageSize = 0, totalHits = 0, results = Array.Empty<object>() });
            }

            var (defaultVersion, defaultLoader) = await ResolveServerDefaultsAsync(
                name,
                serverService,
                profileService,
                cancellationToken);

            var effectiveLoader = string.IsNullOrWhiteSpace(loader) ? defaultLoader : loader;
            var effectiveVersion = string.IsNullOrWhiteSpace(gameVersion) ? defaultVersion : gameVersion;

            var result = await modrinthService.SearchModsAsync(
                query.Trim(),
                index ?? 0,
                pageSize ?? 20,
                effectiveLoader,
                effectiveVersion,
                cancellationToken);

            return Results.Ok(result);
        });

        modrinth.MapGet("/project/{projectId}", async (
            string projectId,
            IModrinthService modrinthService,
            CancellationToken cancellationToken) =>
        {
            var project = await modrinthService.GetProjectAsync(projectId, cancellationToken);
            return project == null
                ? Results.NotFound(new { error = "Project not found" })
                : Results.Ok(project);
        });

        modrinth.MapGet("/project/{projectId}/versions", async (
            string name,
            string projectId,
            [FromQuery] string? loader,
            [FromQuery] string? gameVersion,
            IModrinthService modrinthService,
            IServerService serverService,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            var (defaultVersion, defaultLoader) = await ResolveServerDefaultsAsync(
                name,
                serverService,
                profileService,
                cancellationToken);

            var effectiveLoader = string.IsNullOrWhiteSpace(loader) ? defaultLoader : loader;
            var effectiveVersion = string.IsNullOrWhiteSpace(gameVersion) ? defaultVersion : gameVersion;

            var versions = await modrinthService.GetProjectVersionsAsync(
                projectId,
                effectiveLoader,
                effectiveVersion,
                cancellationToken);

            return Results.Ok(versions);
        });

        modrinth.MapPost("/install", async (
            string name,
            [FromBody] ModrinthInstallRequest request,
            IModrinthService modrinthService,
            IModService modService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.VersionId))
            {
                return Results.BadRequest(new { error = "VersionId is required" });
            }

            var version = await modrinthService.GetVersionAsync(request.VersionId, cancellationToken);
            if (version == null)
            {
                return Results.NotFound(new { error = "Version not found" });
            }

            var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
            if (file == null)
            {
                return Results.BadRequest(new { error = "No files available for this version" });
            }

            await using var stream = await modrinthService.OpenDownloadStreamAsync(file.Url, cancellationToken);
            await modService.SaveModAsync(name, file.FileName, stream, cancellationToken);
            return Results.Ok(new { message = $"Installed mod '{file.FileName}'" });
        });

        var modrinthModpacks = servers.MapGroup("/{name}/mods/modrinth/modpacks");

        modrinthModpacks.MapGet("/search", async (
            string name,
            [FromQuery] string query,
            [FromQuery] int? index,
            [FromQuery] int? pageSize,
            [FromQuery] string? loader,
            [FromQuery] string? gameVersion,
            IModrinthService modrinthService,
            IServerService serverService,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                return Results.Ok(new { index = 0, pageSize = 0, totalHits = 0, results = Array.Empty<object>() });
            }

            var (defaultVersion, defaultLoader) = await ResolveServerDefaultsAsync(
                name,
                serverService,
                profileService,
                cancellationToken);

            var effectiveLoader = string.IsNullOrWhiteSpace(loader) ? defaultLoader : loader;
            var effectiveVersion = string.IsNullOrWhiteSpace(gameVersion) ? defaultVersion : gameVersion;

            var result = await modrinthService.SearchModpacksAsync(
                query.Trim(),
                index ?? 0,
                pageSize ?? 20,
                effectiveLoader,
                effectiveVersion,
                cancellationToken);

            return Results.Ok(result);
        });

        modrinthModpacks.MapGet("/project/{projectId}", async (
            string projectId,
            IModrinthService modrinthService,
            CancellationToken cancellationToken) =>
        {
            var project = await modrinthService.GetProjectAsync(projectId, cancellationToken);
            return project == null
                ? Results.NotFound(new { error = "Project not found" })
                : Results.Ok(project);
        });

        modrinthModpacks.MapGet("/project/{projectId}/versions", async (
            string name,
            string projectId,
            [FromQuery] string? loader,
            [FromQuery] string? gameVersion,
            IModrinthService modrinthService,
            IServerService serverService,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            var (defaultVersion, defaultLoader) = await ResolveServerDefaultsAsync(
                name,
                serverService,
                profileService,
                cancellationToken);

            var effectiveLoader = string.IsNullOrWhiteSpace(loader) ? defaultLoader : loader;
            var effectiveVersion = string.IsNullOrWhiteSpace(gameVersion) ? defaultVersion : gameVersion;

            var versions = await modrinthService.GetProjectVersionsAsync(
                projectId,
                effectiveLoader,
                effectiveVersion,
                cancellationToken);

            return Results.Ok(versions);
        });

        modrinthModpacks.MapPost("/install", async (
            string name,
            [FromBody] ModrinthModpackInstallRequest request,
            IModrinthService modrinthService,
            IBackgroundJobService jobService) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.VersionId))
            {
                return Results.BadRequest(new { error = "ProjectId and VersionId are required" });
            }

            var project = await modrinthService.GetProjectAsync(request.ProjectId, CancellationToken.None);
            var jobId = jobService.QueueJob("modrinth-modpack-install", name, async (services, progress, ct) =>
            {
                var modService = services.GetRequiredService<IModService>();
                await modService.InstallModrinthModpackAsync(
                    name,
                    request.ProjectId,
                    request.VersionId,
                    request.ProjectName ?? project?.Title,
                    request.ProjectVersion,
                    request.LogoUrl ?? project?.IconUrl,
                    progress,
                    ct);
            });

            return Results.Accepted($"/api/v1/jobs/{jobId}", new { jobId, message = "Modrinth modpack install queued" });
        });

        return servers;
    }

    private static async Task<(string? GameVersion, string? Loader)> ResolveServerDefaultsAsync(
        string serverName,
        IServerService serverService,
        IProfileService profileService,
        CancellationToken cancellationToken)
    {
        try
        {
            var config = await serverService.GetServerConfigAsync(serverName, cancellationToken);
            if (string.IsNullOrWhiteSpace(config.Minecraft.Profile))
            {
                return (null, null);
            }

            var profile = await profileService.GetProfileAsync(config.Minecraft.Profile, cancellationToken);
            if (profile == null)
            {
                return (null, null);
            }

            return (profile.Version, MapModLoader(profile.Group));
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? MapModLoader(string? profileGroup)
    {
        if (string.IsNullOrWhiteSpace(profileGroup))
        {
            return null;
        }

        return profileGroup.Trim().ToLowerInvariant() switch
        {
            "forge" => "forge",
            "fabric" => "fabric",
            "quilt" => "quilt",
            "neoforge" => "neoforge",
            "ftb" => "forge",
            _ => null
        };
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

public record ModrinthModpackInstallRequest(
    string ProjectId,
    string VersionId,
    string? ProjectName,
    string? ProjectVersion,
    string? LogoUrl);
