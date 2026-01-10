using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ProfileEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapProfileEndpoints(this RouteGroupBuilder host)
    {
        var profiles = host.MapGroup("/profiles");

        // List all profiles
        profiles.MapGet("", async (
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            var profileList = await profileService.ListProfilesAsync(cancellationToken);
            return Results.Ok(profileList);
        });

        // Get specific profile
        profiles.MapGet("/{id}", async (
            string id,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            var profile = await profileService.GetProfileAsync(id, cancellationToken);
            return profile != null ? Results.Ok(profile) : Results.NotFound(new { error = $"Profile '{id}' not found" });
        });

        // Download profile JAR
        profiles.MapPost("/{id}/download", async (
            string id,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var jarPath = await profileService.DownloadProfileAsync(id, cancellationToken);
                return Results.Ok(new { message = $"Profile '{id}' downloaded", path = jarPath });
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                return Results.Conflict(new { error = $"Download failed: {ex.Message}" });
            }
        });

        // Stream download progress via SSE
        profiles.MapGet("/{id}/download/stream", async (
            HttpContext context,
            string id,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            await foreach (var progress in profileService.StreamDownloadProgressAsync(id, cancellationToken))
            {
                var json = JsonSerializer.Serialize(progress, JsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        });

        // Copy profile to server
        profiles.MapPost("/{id}/copy-to-server", async (
            string id,
            [FromBody] CopyProfileRequest request,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await profileService.CopyProfileToServerAsync(id, request.ServerName, cancellationToken);
                return Results.Ok(new { message = $"Profile '{id}' copied to server '{request.ServerName}'" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        return profiles;
    }
}

public record CopyProfileRequest(string ServerName);
