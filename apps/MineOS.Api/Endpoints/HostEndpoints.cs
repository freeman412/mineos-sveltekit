using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
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
            async (HttpContext context,
                IHostService hostService,
                IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
                CancellationToken cancellationToken) =>
            {
                // Configure SSE headers
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["Connection"] = "keep-alive";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                context.Response.Headers.Remove("Content-Length");

                // Start the response immediately to send headers and prevent buffering
                await context.Response.StartAsync(cancellationToken);

                var intervalMs = 2000;
                if (int.TryParse(context.Request.Query["intervalMs"], out var parsed) && parsed > 100)
                {
                    intervalMs = parsed;
                }

                var interval = TimeSpan.FromMilliseconds(intervalMs);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var metrics = await hostService.GetMetricsAsync(cancellationToken);
                    var payload = JsonSerializer.Serialize(metrics, jsonOptions.Value.SerializerOptions);
                    await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);

                    try
                    {
                        await Task.Delay(interval, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });

        host.MapGet("/servers", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetServersAsync(cancellationToken)));

        host.MapGet("/servers/stream",
            async (HttpContext context,
                IHostService hostService,
                IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
                CancellationToken cancellationToken) =>
            {
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["Connection"] = "keep-alive";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                context.Response.Headers.Remove("Content-Length");

                await context.Response.StartAsync(cancellationToken);

                var intervalMs = 2000;
                if (int.TryParse(context.Request.Query["intervalMs"], out var parsed) && parsed > 100)
                {
                    intervalMs = parsed;
                }

                var interval = TimeSpan.FromMilliseconds(intervalMs);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var servers = await hostService.GetServersAsync(cancellationToken);
                    var payload = JsonSerializer.Serialize(servers, jsonOptions.Value.SerializerOptions);
                    await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);

                    try
                    {
                        await Task.Delay(interval, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });

        host.MapGet("/profiles", async (IProfileService profileService, CancellationToken cancellationToken) =>
            Results.Ok(await profileService.ListProfilesAsync(cancellationToken)));

        host.MapPost("/profiles/{id}/download", async (
            string id,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var jarPath = await profileService.DownloadProfileAsync(id, cancellationToken);
                return Results.Ok(new { message = $"Profile '{id}' downloaded", path = jarPath });
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                return Results.Conflict(new { error = $"Download failed: {ex.Message}" });
            }
        });

        host.MapPost("/profiles/buildtools", async (
            [FromBody] BuildToolsRequest request,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var run = await profileService.StartBuildToolsAsync(request.Group, request.Version, cancellationToken);
                return Results.Accepted($"/api/v1/host/profiles/buildtools/runs/{run.RunId}", run);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        host.MapGet("/profiles/buildtools/runs", async (
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            var runs = await profileService.ListBuildToolsRunsAsync(cancellationToken);
            return Results.Ok(runs);
        });

        host.MapGet("/profiles/buildtools/runs/{runId}", async (
            string runId,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            var run = await profileService.GetBuildToolsRunAsync(runId, cancellationToken);
            return run == null ? Results.NotFound(new { error = "BuildTools run not found" }) : Results.Ok(run);
        });

        host.MapGet("/profiles/buildtools/runs/{runId}/stream",
            async (HttpContext context,
                string runId,
                IProfileService profileService,
                IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
                CancellationToken cancellationToken) =>
            {
                var run = await profileService.GetBuildToolsRunAsync(runId, cancellationToken);
                if (run == null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsJsonAsync(new { error = "BuildTools run not found" }, cancellationToken);
                    return;
                }

                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["Connection"] = "keep-alive";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                context.Response.Headers.Remove("Content-Length");

                await context.Response.StartAsync(cancellationToken);

                await foreach (var entry in profileService.StreamBuildToolsLogAsync(runId, cancellationToken))
                {
                    var payload = JsonSerializer.Serialize(entry, jsonOptions.Value.SerializerOptions);
                    await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            });

        host.MapDelete("/profiles/buildtools/{id}", async (
            string id,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await profileService.DeleteBuildToolsAsync(id, cancellationToken);
                return Results.Ok(new { message = $"Profile '{id}' deleted" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        host.MapPost("/profiles/{id}/copy-to-server", async (
            string id,
            [FromBody] CopyProfileRequest request,
            IProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await profileService.CopyProfileToServerAsync(id, request.ServerName, cancellationToken);
                return Results.Ok(new { message = $"Profile '{id}' copied to server '{request.ServerName}'" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        host.MapGet("/imports", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetImportsAsync(cancellationToken)));

        host.MapPost("/imports/{filename}/create-server", async (
            string filename,
            [FromBody] ImportServerRequest request,
            IImportService importService,
            IServerService serverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await importService.CreateServerFromImportAsync(filename, request.ServerName, cancellationToken);
                var server = await serverService.GetServerAsync(request.ServerName, cancellationToken);
                return Results.Ok(server);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        host.MapGet("/locales", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetLocalesAsync(cancellationToken)));

        host.MapGet("/users", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetUsersAsync(cancellationToken)));

        host.MapGet("/groups", async (IHostService hostService, CancellationToken cancellationToken) =>
            Results.Ok(await hostService.GetGroupsAsync(cancellationToken)));

        return api;
    }
}

public record BuildToolsRequest(string Group, string Version);
public record ImportServerRequest(string ServerName);
