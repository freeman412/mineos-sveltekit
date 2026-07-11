using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Middleware;

public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IApiKeyValidator validator)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<SkipApiKeyAttribute>() != null)
        {
            await _next(context);
            return;
        }

        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
        {
            await _next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing API key.");
            return;
        }

        if (!await validator.IsValidAsync(apiKey.ToString(), context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Invalid API key.");
            return;
        }

        // A valid service key is a full-access (admin) identity. Establish an
        // authenticated principal so downstream authorization policies
        // (.RequireAuthorization and role checks) see an admin — this middleware
        // runs before UseAuthorization. Matches the prior behavior where key
        // requests were treated as full-access, now made explicit for role gates.
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            var identity = new ClaimsIdentity("ApiKey");
            identity.AddClaim(new Claim(ClaimTypes.Name, "service"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
            context.User = new ClaimsPrincipal(identity);
        }

        await _next(context);
    }
}
