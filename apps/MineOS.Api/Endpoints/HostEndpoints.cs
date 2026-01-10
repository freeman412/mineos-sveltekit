using System.Text.Json;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class HostEndpoints
{
    public static RouteGroupBuilder MapHostEndpoints(this RouteGroupBuilder api)
    {
        var host = api.MapGroup("/host");

        host.MapGet("/metrics", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetMetricsAsync(cancellationToken)));

        host.MapGet("/metrics/stream",
            async (HttpContext context, IHostService hostService, CancellationToken cancellationToken) =>
            {
                context.Response.Headers.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";

                var intervalMs = 2000;
                if (int.TryParse(context.Request.Query["intervalMs"], out var parsed) && parsed > 100)
                {
                    intervalMs = parsed;
                }

                await foreach (var metrics in hostService.StreamMetricsAsync(
                                   TimeSpan.FromMilliseconds(intervalMs),
                                   cancellationToken))
                {
                    var payload = JsonSerializer.Serialize(metrics);
                    await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            });

        host.MapGet("/servers", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetServersAsync(cancellationToken)));

        host.MapGet("/profiles", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetProfilesAsync(cancellationToken)));

        host.MapPost("/profiles/{id}/download", () =>
            EndpointHelpers.NotImplementedFeature("host.profiles.download"));

        host.MapPost("/profiles/buildtools", () =>
            EndpointHelpers.NotImplementedFeature("host.profiles.buildtools"));

        host.MapDelete("/profiles/buildtools/{id}", () =>
            EndpointHelpers.NotImplementedFeature("host.profiles.buildtools.delete"));

        host.MapPost("/profiles/{id}/copy-to-server", () =>
            EndpointHelpers.NotImplementedFeature("host.profiles.copy-to-server"));

        host.MapGet("/imports", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetImportsAsync(cancellationToken)));

        host.MapPost("/imports/{filename}/create-server", () =>
            EndpointHelpers.NotImplementedFeature("host.imports.create-server"));

        host.MapGet("/locales", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetLocalesAsync(cancellationToken)));

        host.MapGet("/users", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetUsersAsync(cancellationToken)));

        host.MapGet("/groups", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetGroupsAsync(cancellationToken)));

        return api;
    }
}
