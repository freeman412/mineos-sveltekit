using Microsoft.AspNetCore.Authorization;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.External;
using MineOS.Infrastructure.Services;

namespace MineOS.Api.Endpoints;

public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        var settings = api.MapGroup("/settings");

        // Get all settings (admin only)
        settings.MapGet("/", async (ISettingsService settingsService, CancellationToken cancellationToken) =>
        {
            var all = await settingsService.GetAllAsync(cancellationToken);
            return Results.Ok(all);
        }).RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        // Get a specific setting value
        settings.MapGet("/{key}", async (string key, ISettingsService settingsService, CancellationToken cancellationToken) =>
        {
            var hasValue = await settingsService.HasValueAsync(key, cancellationToken);
            return Results.Ok(new { key, hasValue });
        }).RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        // Set a setting value (admin only)
        settings.MapPut("/{key}", async (string key, SetSettingRequest request, ISettingsService settingsService, CancellationToken cancellationToken) =>
        {
            await settingsService.SetAsync(key, request.Value, cancellationToken);
            return Results.Ok(new { message = $"Setting '{key}' updated" });
        }).RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        // Check if CurseForge is configured (available to all authenticated users)
        settings.MapGet("/curseforge/status", async (CurseForgeClient curseForgeClient, CancellationToken cancellationToken) =>
        {
            var isConfigured = await curseForgeClient.IsConfiguredAsync(cancellationToken);
            return Results.Ok(new { isConfigured });
        }).RequireAuthorization();

        return api;
    }
}

public record SetSettingRequest(string? Value);
