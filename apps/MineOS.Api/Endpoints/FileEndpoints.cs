using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class FileEndpoints
{
    public static RouteGroupBuilder MapFileEndpoints(this RouteGroupBuilder servers)
    {
        // List files in directory
        servers.MapGet("/{name}/files", async (
            string name,
            [FromQuery] string path,
            IFileService fileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var files = await fileService.ListFilesAsync(name, path ?? "/", cancellationToken);
                return Results.Ok(files);
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Read file content
        servers.MapGet("/{name}/files/read", async (
            string name,
            [FromQuery] string path,
            IFileService fileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var content = await fileService.ReadFileAsync(name, path, cancellationToken);
                return Results.Ok(new { content });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
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

        // Write file content
        servers.MapPut("/{name}/files", async (
            string name,
            [FromBody] WriteFileRequest request,
            IFileService fileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await fileService.WriteFileAsync(name, request.Path, request.Content, cancellationToken);
                return Results.Ok(new { message = $"File '{request.Path}' written" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Delete file or directory
        servers.MapDelete("/{name}/files", async (
            string name,
            [FromQuery] string path,
            IFileService fileService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await fileService.DeleteFileAsync(name, path, cancellationToken);
                return Results.Ok(new { message = $"Deleted '{path}'" });
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

        return servers;
    }
}

public record WriteFileRequest(string Path, string Content);
