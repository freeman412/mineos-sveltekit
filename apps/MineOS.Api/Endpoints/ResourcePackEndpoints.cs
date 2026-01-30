using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Api.Endpoints;

public static class ResourcePackEndpoints
{
    public static RouteGroupBuilder MapResourcePackEndpoints(this RouteGroupBuilder servers)
    {
        var modrinth = servers.MapGroup("/{name}/resourcepacks/modrinth");

        modrinth.MapGet("/search", async (
            string name,
            [FromQuery] string query,
            [FromQuery] int? index,
            [FromQuery] int? pageSize,
            [FromQuery] string? gameVersion,
            [FromQuery] string? projectType,
            IModrinthService modrinthService,
            IServerService serverService,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                return Results.Ok(new { index = 0, pageSize = 0, totalHits = 0, results = Array.Empty<object>() });
            }

            var (defaultVersion, _) = await ResolveServerDefaultsAsync(
                name,
                serverService,
                profileService,
                cancellationToken);

            var effectiveVersion = string.IsNullOrWhiteSpace(gameVersion) ? defaultVersion : gameVersion;

            var result = await modrinthService.SearchResourcePacksAsync(
                query.Trim(),
                index ?? 0,
                pageSize ?? 20,
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
            [FromQuery] string? gameVersion,
            IModrinthService modrinthService,
            IServerService serverService,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            var (defaultVersion, _) = await ResolveServerDefaultsAsync(
                name,
                serverService,
                profileService,
                cancellationToken);

            var effectiveVersion = string.IsNullOrWhiteSpace(gameVersion) ? defaultVersion : gameVersion;

            // Resource packs don't use loaders, so pass null
            var versions = await modrinthService.GetProjectVersionsAsync(
                projectId,
                null,
                effectiveVersion,
                cancellationToken);

            return Results.Ok(versions);
        });

        modrinth.MapPost("/install", async (
            string name,
            [FromBody] ModrinthInstallRequest request,
            IModrinthService modrinthService,
            IOptions<HostOptions> hostOptions,
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

            // Save to resourcepacks folder
            var options = hostOptions.Value;
            var resourcePacksPath = Path.Combine(
                options.BaseDirectory,
                options.ServersPathSegment,
                name,
                "resourcepacks");

            Directory.CreateDirectory(resourcePacksPath);

            var filePath = Path.Combine(resourcePacksPath, file.FileName);
            await using var stream = await modrinthService.OpenDownloadStreamAsync(file.Url, cancellationToken);
            await using var fileStream = File.Create(filePath);
            await stream.CopyToAsync(fileStream, cancellationToken);

            return Results.Ok(new { message = $"Installed resource pack '{file.FileName}'" });
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

            return (profile.Version, null);
        }
        catch
        {
            return (null, null);
        }
    }
}

public record ModrinthInstallRequest(string VersionId);
