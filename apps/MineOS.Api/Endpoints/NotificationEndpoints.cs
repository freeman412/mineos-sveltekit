using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Api.Endpoints;

public static class NotificationEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications").WithTags("Notifications");

        // List notifications
        notifications.MapGet("/", async (
            [FromQuery] string? serverName,
            [FromQuery] bool? includeRead,
            [FromQuery] bool? includeDismissed,
            ClaimsPrincipal user,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var query = db.SystemNotifications.AsQueryable();

            // Filter by server name if provided
            if (!string.IsNullOrEmpty(serverName))
            {
                query = query.Where(n => n.ServerName == serverName || n.ServerName == null);
            }

            var access = await ResolveAccessAsync(user, serverAccessService, cancellationToken);
            if (access.Error != null)
            {
                return access.Error;
            }

            if (access.AllowedServers != null && access.UserId.HasValue)
            {
                query = ApplyAccessFilter(query, access.AllowedServers, access.UserId.Value);
            }

            // Filter by read status
            if (includeRead == false)
            {
                query = query.Where(n => !n.IsRead);
            }

            // Filter by dismissed status
            if (includeDismissed == false)
            {
                query = query.Where(n => n.DismissedAt == null);
            }

            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);

            return Results.Ok(notifications);
        });

        // Stream notifications via SSE
        notifications.MapGet("/stream", async (
            HttpContext context,
            [FromQuery] string? serverName,
            [FromQuery] bool? includeRead,
            [FromQuery] bool? includeDismissed,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            try
            {
                context.Response.Headers.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Headers.Connection = "keep-alive";

                await context.Response.StartAsync(cancellationToken);

                string? lastPayload = null;
                var user = context.User;
                var enforceAccess = user?.Identity?.IsAuthenticated == true && !IsAdmin(user);
                var userId = 0;
                if (enforceAccess && !TryGetUserId(user, out userId))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    var query = db.SystemNotifications.AsNoTracking().AsQueryable();

                    if (!string.IsNullOrEmpty(serverName))
                    {
                        query = query.Where(n => n.ServerName == serverName || n.ServerName == null);
                    }

                    if (enforceAccess)
                    {
                        var allowedServers = await serverAccessService.ListServerNamesAsync(userId, cancellationToken);
                        var allowedSet = new HashSet<string>(allowedServers, StringComparer.OrdinalIgnoreCase);
                        query = ApplyAccessFilter(query, allowedSet, userId);
                    }

                    if (includeRead == false)
                    {
                        query = query.Where(n => !n.IsRead);
                    }

                    if (includeDismissed == false)
                    {
                        query = query.Where(n => n.DismissedAt == null);
                    }

                    var snapshot = await query
                        .OrderByDescending(n => n.CreatedAt)
                        .Take(100)
                        .ToListAsync(cancellationToken);

                    var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
                    if (!string.Equals(payload, lastPayload, StringComparison.Ordinal))
                    {
                        await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                        await context.Response.Body.FlushAsync(cancellationToken);
                        lastPayload = payload;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested || context.RequestAborted.IsCancellationRequested)
            {
            }
        });

        // Get notification by ID
        notifications.MapGet("/{id:int}", async (
            int id,
            ClaimsPrincipal user,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var notification = await db.SystemNotifications.FindAsync(new object[] { id }, cancellationToken);
            if (notification != null && !await CanAccessNotificationAsync(notification, user, serverAccessService, cancellationToken))
            {
                return Results.Forbid();
            }
            return notification != null ? Results.Ok(notification) : Results.NotFound();
        });

        // Create notification
        notifications.MapPost("/", async (
            SystemNotification notification,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            notification.CreatedAt = DateTimeOffset.UtcNow;
            db.SystemNotifications.Add(notification);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/notifications/{notification.Id}", notification);
        });

        // Mark as read
        notifications.MapPatch("/{id:int}/read", async (
            int id,
            ClaimsPrincipal user,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var notification = await db.SystemNotifications.FindAsync(new object[] { id }, cancellationToken);
            if (notification == null)
            {
                return Results.NotFound(new { error = "Notification not found" });
            }

            if (!await CanAccessNotificationAsync(notification, user, serverAccessService, cancellationToken))
            {
                return Results.Forbid();
            }

            notification.IsRead = true;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(notification);
        });

        // Dismiss notification
        notifications.MapPatch("/{id:int}/dismiss", async (
            int id,
            ClaimsPrincipal user,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var notification = await db.SystemNotifications.FindAsync(new object[] { id }, cancellationToken);
            if (notification == null)
            {
                return Results.NotFound(new { error = "Notification not found" });
            }

            if (!await CanAccessNotificationAsync(notification, user, serverAccessService, cancellationToken))
            {
                return Results.Forbid();
            }

            notification.DismissedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(notification);
        });

        // Delete notification
        notifications.MapDelete("/{id:int}", async (
            int id,
            ClaimsPrincipal user,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var notification = await db.SystemNotifications.FindAsync(new object[] { id }, cancellationToken);
            if (notification == null)
            {
                return Results.NotFound(new { error = "Notification not found" });
            }

            if (!await CanAccessNotificationAsync(notification, user, serverAccessService, cancellationToken))
            {
                return Results.Forbid();
            }

            db.SystemNotifications.Remove(notification);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        // Bulk delete notifications
        notifications.MapDelete("/", async (
            [FromBody] List<int> ids,
            ClaimsPrincipal user,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var notifications = await db.SystemNotifications
                .Where(n => ids.Contains(n.Id))
                .ToListAsync(cancellationToken);

            var access = await ResolveAccessAsync(user, serverAccessService, cancellationToken);
            if (access.Error != null)
            {
                return access.Error;
            }

            if (access.AllowedServers != null && access.UserId.HasValue)
            {
                notifications = FilterAccessList(notifications, access.AllowedServers, access.UserId.Value);
            }

            if (notifications.Count == 0)
            {
                return Results.Forbid();
            }

            db.SystemNotifications.RemoveRange(notifications);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { deleted = notifications.Count });
        });

        // Bulk dismiss notifications
        notifications.MapPatch("/dismiss", async (
            [FromBody] List<int> ids,
            ClaimsPrincipal user,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var notifications = await db.SystemNotifications
                .Where(n => ids.Contains(n.Id))
                .ToListAsync(cancellationToken);

            var access = await ResolveAccessAsync(user, serverAccessService, cancellationToken);
            if (access.Error != null)
            {
                return access.Error;
            }

            if (access.AllowedServers != null && access.UserId.HasValue)
            {
                notifications = FilterAccessList(notifications, access.AllowedServers, access.UserId.Value);
            }

            if (notifications.Count == 0)
            {
                return Results.Forbid();
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var notification in notifications)
            {
                notification.DismissedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { dismissed = notifications.Count });
        });

        // Bulk mark as read
        notifications.MapPatch("/read", async (
            [FromBody] List<int> ids,
            ClaimsPrincipal user,
            IServerAccessService serverAccessService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var notifications = await db.SystemNotifications
                .Where(n => ids.Contains(n.Id))
                .ToListAsync(cancellationToken);

            var access = await ResolveAccessAsync(user, serverAccessService, cancellationToken);
            if (access.Error != null)
            {
                return access.Error;
            }

            if (access.AllowedServers != null && access.UserId.HasValue)
            {
                notifications = FilterAccessList(notifications, access.AllowedServers, access.UserId.Value);
            }

            if (notifications.Count == 0)
            {
                return Results.Forbid();
            }

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { marked = notifications.Count });
        });

        return api;
    }

    private static bool IsAdmin(ClaimsPrincipal user)
    {
        var role = user.FindFirstValue(ClaimTypes.Role) ?? "user";
        return string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out int userId)
    {
        userId = 0;
        var claim = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? user.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        return int.TryParse(claim, out userId);
    }

    private static async Task<(HashSet<string>? AllowedServers, int? UserId, IResult? Error)> ResolveAccessAsync(
        ClaimsPrincipal user,
        IServerAccessService serverAccessService,
        CancellationToken cancellationToken)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return (null, null, null);
        }

        if (IsAdmin(user))
        {
            return (null, null, null);
        }

        if (!TryGetUserId(user, out var userId))
        {
            return (null, null, Results.Unauthorized());
        }

        var allowed = await serverAccessService.ListServerNamesAsync(userId, cancellationToken);
        return (new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase), userId, null);
    }

    private static IQueryable<SystemNotification> ApplyAccessFilter(
        IQueryable<SystemNotification> query,
        HashSet<string> allowedServers,
        int userId)
    {
        return query.Where(n =>
            n.RecipientUserId == userId ||
            (n.RecipientUserId == null &&
             (n.ServerName == null || allowedServers.Contains(n.ServerName))));
    }

    private static List<SystemNotification> FilterAccessList(
        IEnumerable<SystemNotification> notifications,
        HashSet<string> allowedServers,
        int userId)
    {
        return notifications
            .Where(n => n.RecipientUserId == userId ||
                        (n.RecipientUserId == null &&
                         (n.ServerName == null || allowedServers.Contains(n.ServerName))))
            .ToList();
    }

    private static async Task<bool> CanAccessNotificationAsync(
        SystemNotification notification,
        ClaimsPrincipal user,
        IServerAccessService serverAccessService,
        CancellationToken cancellationToken)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        if (IsAdmin(user))
        {
            return true;
        }

        if (!TryGetUserId(user, out var userId))
        {
            return false;
        }

        if (notification.RecipientUserId == userId)
        {
            return true;
        }

        if (notification.RecipientUserId != null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(notification.ServerName))
        {
            return true;
        }

        var allowed = await serverAccessService.ListServerNamesAsync(userId, cancellationToken);
        return allowed.Any(name => string.Equals(name, notification.ServerName, StringComparison.OrdinalIgnoreCase));
    }
}
