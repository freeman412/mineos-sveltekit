using System.Linq;

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
        return api;
    }
}
