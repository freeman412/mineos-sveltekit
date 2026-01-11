using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder api)
    {
        var players = api.MapGroup("/servers/{serverName}/players")
            .WithTags("Players")
            .RequireAuthorization();

        players.MapGet("/", async (
            string serverName,
            IPlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await playerService.ListPlayersAsync(serverName, cancellationToken);
                return Results.Ok(new { data = result });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("ListPlayers")
          .WithSummary("List players for a server");

        players.MapPost("/{uuid}/whitelist", async (
            string serverName,
            string uuid,
            [FromBody] PlayerNameRequest? request,
            IPlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await playerService.WhitelistPlayerAsync(serverName, uuid, request?.Name, cancellationToken);
                return Results.Ok(new { message = $"Player '{uuid}' whitelisted" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("WhitelistPlayer")
          .WithSummary("Add player to whitelist");

        players.MapDelete("/{uuid}/whitelist", async (
            string serverName,
            string uuid,
            IPlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await playerService.RemoveWhitelistAsync(serverName, uuid, cancellationToken);
                return Results.Ok(new { message = $"Player '{uuid}' removed from whitelist" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("RemoveWhitelist")
          .WithSummary("Remove player from whitelist");

        players.MapPost("/{uuid}/op", async (
            string serverName,
            string uuid,
            [FromBody] OpPlayerRequest? request,
            IPlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await playerService.OpPlayerAsync(
                    serverName,
                    uuid,
                    request?.Name,
                    request?.Level ?? 4,
                    request?.BypassesPlayerLimit ?? true,
                    cancellationToken);
                return Results.Ok(new { message = $"Player '{uuid}' opped" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("OpPlayer")
          .WithSummary("OP a player");

        players.MapPost("/{uuid}/ban", async (
            string serverName,
            string uuid,
            [FromBody] BanPlayerRequest? request,
            IPlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await playerService.BanPlayerAsync(
                    serverName,
                    uuid,
                    request?.Name,
                    request?.Reason,
                    request?.BannedBy,
                    request?.ExpiresAt,
                    cancellationToken);
                return Results.Ok(new { message = $"Player '{uuid}' banned" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("BanPlayer")
          .WithSummary("Ban a player");

        players.MapDelete("/{uuid}/op", async (
            string serverName,
            string uuid,
            IPlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await playerService.DeopPlayerAsync(serverName, uuid, cancellationToken);
                return Results.Ok(new { message = $"Player '{uuid}' deopped" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("DeopPlayer")
          .WithSummary("Remove OP from a player");

        players.MapDelete("/{uuid}/ban", async (
            string serverName,
            string uuid,
            IPlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await playerService.UnbanPlayerAsync(serverName, uuid, cancellationToken);
                return Results.Ok(new { message = $"Player '{uuid}' unbanned" });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("UnbanPlayer")
          .WithSummary("Unban a player");

        players.MapGet("/{uuid}/stats", async (
            string serverName,
            string uuid,
            IPlayerService playerService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var stats = await playerService.GetPlayerStatsAsync(serverName, uuid, cancellationToken);
                return Results.Ok(new { data = stats });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).WithName("GetPlayerStats")
          .WithSummary("Get player stats");

        // Mojang API lookup endpoint (not server-specific)
        var mojang = api.MapGroup("/mojang")
            .WithTags("Mojang")
            .RequireAuthorization();

        mojang.MapGet("/lookup/{username}", async (
            string username,
            IMojangApiService mojangService,
            CancellationToken cancellationToken) =>
        {
            var profile = await mojangService.LookupByUsernameAsync(username, cancellationToken);
            if (profile == null)
            {
                return Results.NotFound(new { error = $"Player '{username}' not found" });
            }
            return Results.Ok(new { data = profile });
        }).WithName("LookupMojangPlayer")
          .WithSummary("Look up a Minecraft player by username from Mojang API");

        return api;
    }
}

public record PlayerNameRequest(string? Name);

public record OpPlayerRequest(string? Name, int? Level, bool? BypassesPlayerLimit);

public record BanPlayerRequest(string? Name, string? Reason, string? BannedBy, DateTimeOffset? ExpiresAt);
