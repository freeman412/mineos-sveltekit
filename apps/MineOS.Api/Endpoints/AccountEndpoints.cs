using Microsoft.AspNetCore.Authorization;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var account = api.MapGroup("/account");

        account.MapGet("/", async (IDeviceAuthService authService, CancellationToken ct) =>
        {
            var linked = await authService.GetLinkedAccountAsync(ct);
            return linked != null
                ? Results.Ok(new { linked = true, userId = linked.UserId, linkedAt = linked.LinkedAt, expiresAt = linked.ExpiresAt })
                : Results.Ok(new { linked = false });
        })
        .RequireAuthorization();

        account.MapGet("/link", async (IDeviceAuthService authService, CancellationToken ct) =>
        {
            try
            {
                var response = await authService.InitiateAsync(ct);
                return Results.Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(502);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        account.MapGet("/link/status", async (IDeviceAuthService authService, CancellationToken ct) =>
        {
            var status = await authService.GetStatusAsync(ct);
            return Results.Ok(status);
        })
        .RequireAuthorization();

        account.MapDelete("/link", async (IDeviceAuthService authService, CancellationToken ct) =>
        {
            await authService.UnlinkAsync(ct);
            return Results.Ok(new { message = "Account unlinked" });
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        return api;
    }
}
