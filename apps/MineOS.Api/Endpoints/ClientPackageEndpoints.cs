using Microsoft.Extensions.DependencyInjection;
using MineOS.Api.Middleware;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class ClientPackageEndpoints
{
    public static RouteGroupBuilder MapClientPackageEndpoints(this RouteGroupBuilder servers)
    {
        servers.MapGet("/{name}/client-packages", async (
            string name,
            IClientPackageService clientPackageService,
            CancellationToken cancellationToken) =>
        {
            var packages = await clientPackageService.ListClientPackagesAsync(name, cancellationToken);
            return Results.Ok(packages);
        });

        servers.MapPost("/{name}/client-packages", async (
            string name,
            IClientPackageService clientPackageService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var filename = await clientPackageService.CreateClientPackageAsync(name, cancellationToken);
                return Results.Ok(new
                {
                    filename,
                    downloadUrl = $"/api/v1/servers/{name}/client-packages/{filename}/download"
                });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        servers.MapDelete("/{name}/client-packages/{filename}", async (
            string name,
            string filename,
            IClientPackageService clientPackageService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await clientPackageService.DeleteClientPackageAsync(name, filename, cancellationToken);
                return Results.Ok(new { message = $"Client package '{filename}' deleted" });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        servers.MapGet("/{name}/client-packages/{filename}/download", async (
            string name,
            string filename,
            IClientPackageService clientPackageService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var path = await clientPackageService.GetClientPackagePathAsync(name, filename, cancellationToken);
                return Results.File(path, "application/zip", filename);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithMetadata(new SkipApiKeyAttribute());

        return servers;
    }
}
