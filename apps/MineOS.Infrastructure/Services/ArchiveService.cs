using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class ArchiveService : IArchiveService
{
    private readonly ILogger<ArchiveService> _logger;
    private readonly HostOptions _hostOptions;

    public ArchiveService(
        ILogger<ArchiveService> logger,
        IOptions<HostOptions> hostOptions)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetArchivePath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "archives");

    public async Task<IEnumerable<ArchiveEntryDto>> ListArchivesAsync(string serverName, CancellationToken cancellationToken)
    {
        var archivePath = GetArchivePath(serverName);
        if (!Directory.Exists(archivePath))
        {
            return Enumerable.Empty<ArchiveEntryDto>();
        }

        var archives = new List<ArchiveEntryDto>();
        var files = Directory.GetFiles(archivePath, "*.tar.gz");

        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            archives.Add(new ArchiveEntryDto(
                fileInfo.LastWriteTimeUtc,
                fileInfo.Length,
                fileInfo.Name
            ));
        }

        return await Task.FromResult(archives.OrderByDescending(a => a.Time));
    }

    public async Task<string> CreateArchiveAsync(string serverName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        var archivePath = GetArchivePath(serverName);

        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        // Ensure archive directory exists
        Directory.CreateDirectory(archivePath);

        // Create archive filename with timestamp
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var archiveFilename = $"{serverName}_{timestamp}.tar.gz";
        var archiveFullPath = Path.Combine(archivePath, archiveFilename);

        // Use tar to create compressed archive
        // Exclude the archives directory to prevent "file changed as we read it" errors
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-czf \"{archiveFullPath}\" --exclude=\"archives\" -C \"{Path.GetDirectoryName(serverPath)}\" \"{Path.GetFileName(serverPath)}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start tar process");
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Archive creation failed: {error}");
        }

        _logger.LogInformation("Created archive {Filename} for server {ServerName}", archiveFilename, serverName);

        return archiveFilename;
    }

    public Task DeleteArchiveAsync(string serverName, string filename, CancellationToken cancellationToken)
    {
        var archivePath = GetArchivePath(serverName);
        var fullPath = Path.Combine(archivePath, filename);

        // Validate filename to prevent directory traversal
        if (Path.GetFileName(filename) != filename || !filename.EndsWith(".tar.gz"))
        {
            throw new ArgumentException("Invalid archive filename");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Archive '{filename}' not found");
        }

        File.Delete(fullPath);
        _logger.LogInformation("Deleted archive {Filename} for server {ServerName}", filename, serverName);

        return Task.CompletedTask;
    }

    public Task<string> GetArchivePathAsync(string serverName, string filename, CancellationToken cancellationToken)
    {
        var archivePath = GetArchivePath(serverName);
        var fullPath = Path.Combine(archivePath, filename);

        // Validate filename to prevent directory traversal
        if (Path.GetFileName(filename) != filename || !filename.EndsWith(".tar.gz"))
        {
            throw new ArgumentException("Invalid archive filename");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Archive '{filename}' not found");
        }

        return Task.FromResult(fullPath);
    }
}
