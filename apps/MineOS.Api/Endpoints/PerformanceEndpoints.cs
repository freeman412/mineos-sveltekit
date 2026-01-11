using System.Text.Json;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class PerformanceEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapPerformanceEndpoints(this IEndpointRouteBuilder api)
    {
        var performance = api.MapGroup("/servers/{name}/performance");

        performance.MapGet("/realtime", async (
            string name,
            IPerformanceService performanceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var sample = await performanceService.GetRealtimeAsync(name, cancellationToken);
                return Results.Ok(sample);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        performance.MapGet("/history", async (
            string name,
            int? minutes,
            IPerformanceService performanceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var windowMinutes = minutes.GetValueOrDefault(60);
                if (windowMinutes < 5)
                {
                    windowMinutes = 5;
                }
                else if (windowMinutes > 1440)
                {
                    windowMinutes = 1440;
                }

                var history = await performanceService.GetHistoryAsync(
                    name,
                    TimeSpan.FromMinutes(windowMinutes),
                    cancellationToken);
                return Results.Ok(history);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        performance.MapGet("/stream", async (
            HttpContext context,
            string name,
            IPerformanceService performanceService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            await foreach (var sample in performanceService.StreamRealtimeAsync(
                               name,
                               TimeSpan.FromSeconds(2),
                               cancellationToken))
            {
                var json = JsonSerializer.Serialize(sample, JsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        });

        return api;
    }
}
