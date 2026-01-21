using System.Text.Json;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class JobEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder api)
    {
        var jobs = api.MapGroup("/jobs");

        // List all active jobs
        jobs.MapGet("/", (IBackgroundJobService jobService, IForgeService forgeService) =>
        {
            var activeJobs = jobService.GetActiveJobs();
            var activeModpacks = jobService.GetActiveModpackInstalls();
            var activeForgeInstalls = forgeService.GetActiveInstalls();
            return Results.Ok(new
            {
                jobs = activeJobs,
                modpackInstalls = activeModpacks,
                forgeInstalls = activeForgeInstalls
            });
        });

        // Stream active job lists via SSE
        jobs.MapGet("/stream", async (
            HttpContext context,
            IBackgroundJobService jobService,
            IForgeService forgeService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            string? lastPayload = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var snapshot = new
                {
                    jobs = jobService.GetActiveJobs(),
                    modpackInstalls = jobService.GetActiveModpackInstalls(),
                    forgeInstalls = forgeService.GetActiveInstalls()
                };

                var payload = JsonSerializer.Serialize(snapshot, JsonOptions);
                if (!string.Equals(payload, lastPayload, StringComparison.Ordinal))
                {
                    await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                    lastPayload = payload;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        });

        // Get job status
        jobs.MapGet("/{jobId}", (
            string jobId,
            IBackgroundJobService jobService) =>
        {
            var status = jobService.GetJobStatus(jobId);
            return status != null ? Results.Ok(status) : Results.NotFound(new { error = "Job not found" });
        });

        // Stream job progress via SSE
        jobs.MapGet("/{jobId}/stream", async (
            HttpContext context,
            string jobId,
            IBackgroundJobService jobService,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await context.Response.StartAsync(cancellationToken);

            await foreach (var progress in jobService.StreamJobProgressAsync(jobId, cancellationToken))
            {
                var json = JsonSerializer.Serialize(progress, JsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        });

        return jobs;
    }
}
