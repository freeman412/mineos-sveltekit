using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class PluginEndpoints
{
    public static RouteGroupBuilder MapPluginEndpoints(this RouteGroupBuilder servers)
    {
        servers.MapGet("/{name}/plugins", async (
            string name,
            IPluginService pluginService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var plugins = await pluginService.ListPluginsAsync(name, cancellationToken);
                return Results.Ok(plugins);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        servers.MapPost("/{name}/plugins/upload", async (
            string name,
            HttpRequest request,
            IPluginService pluginService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest(new { error = "Plugin file is required" });
                }

                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files.FirstOrDefault();
                if (file == null)
                {
                    return Results.BadRequest(new { error = "Plugin file is required" });
                }

                await using var stream = file.OpenReadStream();
                await pluginService.SavePluginAsync(name, file.FileName, stream, cancellationToken);
                return Results.Ok(new { message = $"Uploaded plugin '{file.FileName}'" });
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

        servers.MapDelete("/{name}/plugins/{filename}", async (
            string name,
            string filename,
            IPluginService pluginService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await pluginService.DeletePluginAsync(name, filename, cancellationToken);
                return Results.Ok(new { message = $"Deleted plugin '{filename}'" });
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

        servers.MapGet("/{name}/plugins/{filename}/download", async (
            string name,
            string filename,
            IPluginService pluginService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var path = await pluginService.GetPluginPathAsync(name, filename, cancellationToken);
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

        servers.MapPost("/{name}/plugins/{filename}/enable", async (
            string name,
            string filename,
            IPluginService pluginService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await pluginService.SetPluginEnabledAsync(name, filename, true, cancellationToken);
                return Results.Ok(new { message = $"Enabled plugin '{filename}'" });
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

        servers.MapPost("/{name}/plugins/{filename}/disable", async (
            string name,
            string filename,
            IPluginService pluginService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await pluginService.SetPluginEnabledAsync(name, filename, false, cancellationToken);
                return Results.Ok(new { message = $"Disabled plugin '{filename}'" });
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

        var modrinth = servers.MapGroup("/{name}/plugins/modrinth");

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

            var result = await modrinthService.SearchPluginsAsync(
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
            IPluginService pluginService,
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
            await pluginService.SavePluginAsync(name, file.FileName, stream, cancellationToken);
            return Results.Ok(new { message = $"Installed plugin '{file.FileName}'" });
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

            return (profile.Version, MapLoader(profile.Group));
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? MapLoader(string? profileGroup)
    {
        if (string.IsNullOrWhiteSpace(profileGroup))
        {
            return null;
        }

        return profileGroup.Trim().ToLowerInvariant() switch
        {
            "paper" => "paper",
            "spigot" => "spigot",
            "bukkit" => "bukkit",
            "purpur" => "purpur",
            "folia" => "folia",
            "velocity" => "velocity",
            "waterfall" => "waterfall",
            "bungeecord" => "bungeecord",
            _ => null
        };
    }
}

public record ModrinthInstallRequest(string VersionId);
