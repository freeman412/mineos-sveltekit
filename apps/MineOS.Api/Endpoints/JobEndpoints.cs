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
        jobs.MapGet("/", (IBackgroundJobService jobService) =>
        {
            var activeJobs = jobService.GetActiveJobs();
            var activeModpacks = jobService.GetActiveModpackInstalls();
            return Results.Ok(new
            {
                jobs = activeJobs,
                modpackInstalls = activeModpacks
            });
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
