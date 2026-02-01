using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Persistence;

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

        auth.MapPost("/logout", (HttpContext ctx) =>
            {
                // JWT is stateless; cookie-based logout is handled by the SvelteKit frontend.
                // This endpoint exists so API clients get a 200 instead of a 501.
                ctx.Response.Cookies.Delete("auth_token", new CookieOptions
                {
                    Path = "/",
                    HttpOnly = true,
                    Secure = ctx.Request.IsHttps,
                    SameSite = SameSiteMode.Lax
                });
                return Results.Ok(new { message = "Logged out" });
            })
            .AllowAnonymous()
            .WithMetadata(new MineOS.Api.Middleware.SkipApiKeyAttribute());

        auth.MapGet("/me", async (
                System.Security.Claims.ClaimsPrincipal user,
                AppDbContext db,
                CancellationToken cancellationToken) =>
            {
                var userIdClaim = user.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? user.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
                if (int.TryParse(userIdClaim, out var userId))
                {
                    var dbUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                    if (dbUser != null)
                    {
                        return Results.Ok(new { username = dbUser.Username, role = dbUser.Role });
                    }
                }

                var username = user.Identity?.Name ?? string.Empty;
                var role = user.Claims.FirstOrDefault(c => c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value ?? "user";
                return Results.Ok(new { username, role });
            })
            .RequireAuthorization()
            .WithMetadata(new MineOS.Api.Middleware.SkipApiKeyAttribute());

        auth.MapPatch("/me", async (
            UpdateSelfRequestDto request,
            System.Security.Claims.ClaimsPrincipal user,
            IUserService userService,
            CancellationToken cancellationToken) =>
        {
            var userIdClaim = user.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? user.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var updated = await userService.UpdateSelfAsync(userId, request, cancellationToken);
                return Results.Ok(new { username = updated.Username, role = updated.Role });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
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

        auth.MapDelete("/users/{id:int}", async (
            int id,
            IUserService userService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await userService.DeleteUserAsync(id, cancellationToken);
                return Results.Ok(new { message = "User deleted" });
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        return api;
    }
}
