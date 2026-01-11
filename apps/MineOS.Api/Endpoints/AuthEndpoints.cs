using System.Linq;
using Microsoft.AspNetCore.Authorization;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/login", async (MineOS.Application.Dtos.LoginRequestDto request,
                MineOS.Application.Interfaces.IAuthService authService,
                CancellationToken cancellationToken) =>
            {
                var result = await authService.LoginAsync(request, cancellationToken);
                return result == null ? Results.Unauthorized() : Results.Ok(result);
            })
            .AllowAnonymous()
            .WithMetadata(new MineOS.Api.Middleware.SkipApiKeyAttribute());

        auth.MapPost("/logout", () => EndpointHelpers.NotImplementedFeature("auth.logout"))
            .AllowAnonymous()
            .WithMetadata(new MineOS.Api.Middleware.SkipApiKeyAttribute());

        auth.MapGet("/me", (System.Security.Claims.ClaimsPrincipal user) =>
            {
                var username = user.Identity?.Name ?? string.Empty;
                var role = user.Claims.FirstOrDefault(c => c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value ?? "user";
                return Results.Ok(new { username, role });
            })
            .RequireAuthorization()
            .WithMetadata(new MineOS.Api.Middleware.SkipApiKeyAttribute());

        auth.MapGet("/users", async (IUserService userService, CancellationToken cancellationToken) =>
            Results.Ok(await userService.ListUsersAsync(cancellationToken)))
            .RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        auth.MapPost("/users", async (
            CreateUserRequestDto request,
            IUserService userService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await userService.CreateUserAsync(request, cancellationToken);
                return Results.Ok(created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        auth.MapPatch("/users/{id:int}", async (
            int id,
            UpdateUserRequestDto request,
            IUserService userService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await userService.UpdateUserAsync(id, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });
        return api;
    }
}
