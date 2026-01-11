using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class ImportService : IImportService
{
    private readonly HostOptions _options;
    private readonly ILogger<ImportService> _logger;

    public ImportService(IOptions<HostOptions> options, ILogger<ImportService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private string GetImportPath() =>
        Path.Combine(_options.BaseDirectory, _options.ImportPathSegment);

    private string GetServersPath() =>
        Path.Combine(_options.BaseDirectory, _options.ServersPathSegment);

    public async Task<string> SaveImportAsync(string filename, Stream content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Filename is required");
        }

        var safeName = Path.GetFileName(filename);
        if (!string.Equals(safeName, filename, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid filename");
        }

        if (!safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            !safeName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) &&
            !safeName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only .zip, .tar.gz, or .tgz files are supported");
        }

        var importPath = GetImportPath();
        Directory.CreateDirectory(importPath);

        var targetPath = Path.Combine(importPath, safeName);
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(target, cancellationToken);

        _logger.LogInformation("Uploaded import archive {Filename}", safeName);
        return targetPath;
    }

    public async Task<string> CreateServerFromImportAsync(string filename, string serverName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            throw new ArgumentException("Server name is required");
        }

        if (Path.GetFileName(serverName) != serverName)
        {
            throw new ArgumentException("Invalid server name");
        }

        if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename)
        {
            throw new ArgumentException("Invalid import filename");
        }

        var importPath = GetImportPath();
        var archivePath = Path.Combine(importPath, filename);

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Import file '{filename}' not found");
        }

        var serverPath = Path.Combine(GetServersPath(), serverName);
        if (Directory.Exists(serverPath))
        {
            throw new InvalidOperationException($"Server '{serverName}' already exists");
        }

        Directory.CreateDirectory(GetServersPath());

        var tempDir = Path.Combine(importPath, $".extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            if (filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archivePath, tempDir);
            }
            else if (filename.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                     filename.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xzf \"{archivePath}\" -C \"{tempDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start tar extraction");
                }

                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    throw new InvalidOperationException($"Import extraction failed: {error}");
                }
            }
            else
            {
                throw new ArgumentException("Unsupported import archive type");
            }

            var extractedRoot = ResolveExtractedRoot(tempDir);
            if (string.Equals(extractedRoot, tempDir, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(tempDir, serverPath);
            }
            else
            {
                Directory.Move(extractedRoot, serverPath);
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }

            _logger.LogInformation("Imported server {ServerName} from {Filename}", serverName, filename);
            return serverPath;
        }
        catch
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
            throw;
        }
    }

    private static string ResolveExtractedRoot(string tempDir)
    {
        var directories = Directory.GetDirectories(tempDir);
        var files = Directory.GetFiles(tempDir);

        if (directories.Length == 1 && files.Length == 0)
        {
            return directories[0];
        }

        return tempDir;
    }
}
