using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class FileService : IFileService
{
    private readonly ILogger<FileService> _logger;
    private readonly HostOptions _hostOptions;

    public FileService(
        ILogger<FileService> logger,
        IOptions<HostOptions> hostOptions)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetSafePath(string serverName, string relativePath)
    {
        var serverPath = GetServerPath(serverName);
        var fullPath = Path.Combine(serverPath, relativePath.TrimStart('/'));
        var normalizedPath = Path.GetFullPath(fullPath);

        // Prevent directory traversal
        if (!normalizedPath.StartsWith(serverPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid path - directory traversal detected");
        }

        return normalizedPath;
    }

    public async Task<IReadOnlyList<FileEntryDto>> ListFilesAsync(string serverName, string path, CancellationToken cancellationToken)
    {
        var fullPath = GetSafePath(serverName, path);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        var entries = new List<FileEntryDto>();

        // Add directories
        foreach (var dir in Directory.GetDirectories(fullPath))
        {
            var dirInfo = new DirectoryInfo(dir);
            entries.Add(new FileEntryDto(
                dirInfo.Name,
                true,
                0,
                dirInfo.LastWriteTimeUtc
            ));
        }

        // Add files
        foreach (var file in Directory.GetFiles(fullPath))
        {
            var fileInfo = new FileInfo(file);
            entries.Add(new FileEntryDto(
                fileInfo.Name,
                false,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc
            ));
        }

        var ordered = entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name)
            .ToList();

        return await Task.FromResult<IReadOnlyList<FileEntryDto>>(ordered);
    }

    public async Task<FileContentDto> ReadFileAsync(string serverName, string path, CancellationToken cancellationToken)
    {
        var fullPath = GetSafePath(serverName, path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }

        var fileInfo = new FileInfo(fullPath);
        // Limit file size to 1MB for safety
        if (fileInfo.Length > 1024 * 1024)
        {
            throw new InvalidOperationException("File too large to read (max 1MB)");
        }

        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        return new FileContentDto(path, content, fileInfo.Length, fileInfo.LastWriteTimeUtc);
    }

    public async Task<byte[]> ReadFileBytesAsync(string serverName, string path, CancellationToken cancellationToken)
    {
        var fullPath = GetSafePath(serverName, path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }

        var fileInfo = new FileInfo(fullPath);
        // Limit file size to 5MB for images
        if (fileInfo.Length > 5 * 1024 * 1024)
        {
            throw new InvalidOperationException("File too large to read (max 5MB)");
        }

        return await File.ReadAllBytesAsync(fullPath, cancellationToken);
    }

    public async Task WriteFileAsync(string serverName, string path, string content, CancellationToken cancellationToken)
    {
        var fullPath = GetSafePath(serverName, path);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            await OwnershipHelper.ChangeOwnershipAsync(directory, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger, cancellationToken);
        }

        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(fullPath, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger, cancellationToken);
        _logger.LogInformation("Wrote file {Path} for server {ServerName}", path, serverName);
    }

    public async Task WriteFileBytesAsync(string serverName, string path, byte[] content, CancellationToken cancellationToken)
    {
        var fullPath = GetSafePath(serverName, path);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            await OwnershipHelper.ChangeOwnershipAsync(directory, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger, cancellationToken);
        }

        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(fullPath, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger, cancellationToken);
        _logger.LogInformation("Wrote binary file {Path} for server {ServerName}", path, serverName);
    }

    public Task DeleteFileAsync(string serverName, string path, CancellationToken cancellationToken)
    {
        var fullPath = GetSafePath(serverName, path);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted file {Path} for server {ServerName}", path, serverName);
        }
        else if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            _logger.LogInformation("Deleted directory {Path} for server {ServerName}", path, serverName);
        }
        else
        {
            throw new FileNotFoundException($"File or directory not found: {path}");
        }

        return Task.CompletedTask;
    }

}
