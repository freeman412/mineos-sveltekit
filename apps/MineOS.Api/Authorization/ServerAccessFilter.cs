using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Authorization;

public sealed class ServerAccessFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return await next(context);
        }

        var role = user.FindFirstValue(ClaimTypes.Role) ?? "user";
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return await next(context);
        }

        if (!TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        if (!TryGetServerName(httpContext, out var serverName))
        {
            return await next(context);
        }

        var permission = ResolvePermission(httpContext);
        var accessService = httpContext.RequestServices.GetRequiredService<IServerAccessService>();
        var access = await accessService.GetAccessAsync(userId, serverName, httpContext.RequestAborted);

        if (access == null || !HasPermission(access, permission))
        {
            return Results.Forbid();
        }

        return await next(context);
    }

    private static bool TryGetServerName(HttpContext context, out string serverName)
    {
        serverName = string.Empty;
        if (!context.Request.RouteValues.TryGetValue("name", out var value) || value == null)
        {
            return false;
        }

        serverName = value.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(serverName);
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out int userId)
    {
        userId = 0;
        var claim = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? user.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        return int.TryParse(claim, out userId);
    }

    private static ServerPermission ResolvePermission(HttpContext context)
    {
        var requirement = context.GetEndpoint()?.Metadata.GetMetadata<ServerAccessRequirement>();
        if (requirement != null)
        {
            return requirement.Permission;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Contains("/console", StringComparison.OrdinalIgnoreCase))
        {
            return ServerPermission.Console;
        }

        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            return ServerPermission.View;
        }

        return ServerPermission.Control;
    }

    private static bool HasPermission(Application.Dtos.ServerAccessDto access, ServerPermission permission)
    {
        return permission switch
        {
            ServerPermission.View => access.CanView || access.CanControl || access.CanConsole,
            ServerPermission.Control => access.CanControl,
            ServerPermission.Console => access.CanConsole,
            _ => false
        };
    }
}
