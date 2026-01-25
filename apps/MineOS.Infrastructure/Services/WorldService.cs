using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class WorldService : IWorldService
{
    private readonly ILogger<WorldService> _logger;
    private readonly HostOptions _hostOptions;

    public WorldService(
        ILogger<WorldService> logger,
        IOptions<HostOptions> hostOptions)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
    }

    private bool IsWorldFolder(string path)
    {
        // A folder is a world if it contains level.dat or session.lock
        return File.Exists(Path.Combine(path, "level.dat")) ||
               File.Exists(Path.Combine(path, "session.lock"));
    }

    private string DetermineWorldType(string worldName, string worldPath)
    {
        // Check for dimension folders (indicates this is a Bukkit/Spigot/Paper world container)
        var dimNether = Path.Combine(worldPath, "DIM-1");
        var dimEnd = Path.Combine(worldPath, "DIM1");
        var dimAether = Path.Combine(worldPath, "DIM-4");
        var dimAetherAlt = Path.Combine(worldPath, "DIM4");
        var dimAetherIi = Path.Combine(worldPath, "DIM2");
        var dimAetherIiAlt = Path.Combine(worldPath, "DIM-2");

        if (worldName.Contains("aether_ii", StringComparison.OrdinalIgnoreCase) ||
            worldName.Contains("aetherii", StringComparison.OrdinalIgnoreCase) ||
            worldName.Contains("aether2", StringComparison.OrdinalIgnoreCase) ||
            worldName == "DIM2" ||
            worldName == "DIM-2")
        {
            return "Aether II";
        }

        if (worldName.Contains("aether", StringComparison.OrdinalIgnoreCase) ||
            worldName == "DIM-4" ||
            worldName == "DIM4")
        {
            return "Aether";
        }

        // Vanilla-style separate world folders
        if (worldName.Contains("nether", StringComparison.OrdinalIgnoreCase) ||
            worldName == "world_nether" ||
            worldName == "DIM-1")
        {
            return "Nether";
        }

        if (worldName.Contains("end", StringComparison.OrdinalIgnoreCase) ||
            worldName == "world_the_end" ||
            worldName == "DIM1")
        {
            return "The End";
        }

        // If it has dimension folders inside, it's the main world container
        if (Directory.Exists(dimNether) ||
            Directory.Exists(dimEnd) ||
            Directory.Exists(dimAether) ||
            Directory.Exists(dimAetherAlt) ||
            Directory.Exists(dimAetherIi) ||
            Directory.Exists(dimAetherIiAlt))
        {
            return "Overworld (Multi-Dimension)";
        }

        // Default to Overworld
        return "Overworld";
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetWorldPath(string serverName, string worldName)
    {
        var serverPath = GetServerPath(serverName);
        var worldPath = Path.Combine(serverPath, worldName);

        // Security check: prevent directory traversal
        var normalizedPath = Path.GetFullPath(worldPath);
        if (!normalizedPath.StartsWith(serverPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid world name - directory traversal detected");
        }

        return worldPath;
    }

    public async Task<IReadOnlyList<WorldDto>> ListWorldsAsync(string serverName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            return Array.Empty<WorldDto>();
        }

        var worlds = new List<WorldDto>();

        // Scan all directories in the server folder
        var directories = Directory.GetDirectories(serverPath);

        foreach (var dirPath in directories)
        {
            var worldName = Path.GetFileName(dirPath);

            // Skip non-world folders
            if (!IsWorldFolder(dirPath))
            {
                continue;
            }

            // Skip common non-world folders even if they might have world files
            if (worldName.Equals("plugins", StringComparison.OrdinalIgnoreCase) ||
                worldName.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                worldName.Equals("crash-reports", StringComparison.OrdinalIgnoreCase) ||
                worldName.Equals("backups", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var type = DetermineWorldType(worldName, dirPath);
            var size = await GetWorldSizeAsync(serverName, worldName, cancellationToken);
            var lastModified = Directory.GetLastWriteTimeUtc(dirPath);

            worlds.Add(new WorldDto(
                worldName,
                type,
                size,
                lastModified));
        }

        // Sort worlds: Overworld first, then Nether, End, Aether, Aether II, then custom
        return worlds
            .OrderBy(w => w.Type switch
            {
                "Overworld" => 0,
                "Overworld (Multi-Dimension)" => 0,
                "Nether" => 1,
                "The End" => 2,
                "Aether" => 3,
                "Aether II" => 4,
                _ => 5
            })
            .ThenBy(w => w.Name)
            .ToList();
    }

    public async Task<WorldInfoDto> GetWorldInfoAsync(string serverName, string worldName, CancellationToken cancellationToken)
    {
        var worldPath = GetWorldPath(serverName, worldName);
        if (!Directory.Exists(worldPath))
        {
            throw new FileNotFoundException($"World '{worldName}' not found for server '{serverName}'");
        }

        var type = DetermineWorldType(worldName, worldPath);

        var size = await GetWorldSizeAsync(serverName, worldName, cancellationToken);
        var lastModified = Directory.GetLastWriteTimeUtc(worldPath);

        // Try to read level.dat for world properties
        var levelDatPath = Path.Combine(worldPath, "level.dat");
        string? seed = null;
        string? levelName = null;
        string? gameMode = null;
        string? difficulty = null;
        bool? hardcore = null;

        if (File.Exists(levelDatPath))
        {
            // Note: Reading NBT data would require an NBT parser library
            // For now, we'll leave these as null
            // TODO: Add fNbt or similar library to parse level.dat
        }

        var fileCount = Directory.GetFiles(worldPath, "*", SearchOption.AllDirectories).Length;
        var directoryCount = Directory.GetDirectories(worldPath, "*", SearchOption.AllDirectories).Length;

        return new WorldInfoDto(
            worldName,
            type,
            size,
            seed,
            levelName,
            gameMode,
            difficulty,
            hardcore,
            lastModified,
            fileCount,
            directoryCount);
    }

    public async Task<Stream> DownloadWorldAsync(string serverName, string worldName, CancellationToken cancellationToken)
    {
        var worldPath = GetWorldPath(serverName, worldName);
        if (!Directory.Exists(worldPath))
        {
            throw new FileNotFoundException($"World '{worldName}' not found for server '{serverName}'");
        }

        _logger.LogInformation("Creating ZIP archive for world {WorldName} on server {ServerName}", worldName, serverName);

        // Create a temporary file path for the ZIP
        var tempFile = Path.GetTempFileName();

        // Delete the empty file created by GetTempFileName() since ZipFile.CreateFromDirectory expects the file to not exist
        File.Delete(tempFile);

        try
        {
            // Create ZIP archive
            await Task.Run(() =>
            {
                ZipFile.CreateFromDirectory(worldPath, tempFile, CompressionLevel.Fastest, false);
            }, cancellationToken);

            // Read the ZIP file into a MemoryStream
            var memoryStream = new MemoryStream();
            using (var fileStream = File.OpenRead(tempFile))
            {
                await fileStream.CopyToAsync(memoryStream, cancellationToken);
            }

            // Delete temp file
            File.Delete(tempFile);

            memoryStream.Position = 0;
            _logger.LogInformation("Created ZIP archive for world {WorldName} ({Size} bytes)", worldName, memoryStream.Length);
            return memoryStream;
        }
        catch
        {
            // Clean up temp file on error
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            throw;
        }
    }

    public async Task UploadWorldAsync(string serverName, string worldName, Stream zipStream, CancellationToken cancellationToken)
    {
        var worldPath = GetWorldPath(serverName, worldName);
        var serverPath = GetServerPath(serverName);

        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        _logger.LogInformation("Uploading world {WorldName} to server {ServerName}", worldName, serverName);

        // Create a temporary directory for extraction
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Save ZIP to temp file
            var tempZipFile = Path.Combine(tempDir, "world.zip");
            using (var fileStream = File.Create(tempZipFile))
            {
                await zipStream.CopyToAsync(fileStream, cancellationToken);
            }

            // Extract ZIP
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(tempZipFile, tempDir);
            }, cancellationToken);

            // Delete the ZIP file
            File.Delete(tempZipFile);

            // Delete existing world if it exists
            if (Directory.Exists(worldPath))
            {
                _logger.LogInformation("Deleting existing world {WorldName}", worldName);
                Directory.Delete(worldPath, true);
            }

            // Move extracted files to world path
            var extractedDirs = Directory.GetDirectories(tempDir);
            if (extractedDirs.Length == 1)
            {
                // ZIP contains a single folder - use its contents
                Directory.Move(extractedDirs[0], worldPath);
            }
            else
            {
                // ZIP contains multiple items at root - move the entire temp dir
                Directory.Move(tempDir, worldPath);
                tempDir = null; // Prevent cleanup
            }

            _logger.LogInformation("Successfully uploaded world {WorldName}", worldName);
        }
        finally
        {
            // Clean up temp directory if it still exists
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temporary directory {TempDir}", tempDir);
                }
            }
        }
    }

    public async Task<string> UploadNewWorldAsync(string serverName, Stream zipStream, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);

        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        _logger.LogInformation("Uploading new world to server {ServerName}", serverName);

        // Create a temporary directory for extraction
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Save ZIP to temp file
            var tempZipFile = Path.Combine(tempDir, "world.zip");
            using (var fileStream = File.Create(tempZipFile))
            {
                await zipStream.CopyToAsync(fileStream, cancellationToken);
            }

            // Extract ZIP
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(tempZipFile, tempDir);
            }, cancellationToken);

            // Delete the ZIP file
            File.Delete(tempZipFile);

            // Find the world folder (must contain level.dat)
            string worldFolderPath = null;
            string worldName = null;

            var extractedDirs = Directory.GetDirectories(tempDir);
            if (extractedDirs.Length == 1)
            {
                // Single folder in ZIP - check if it's a world
                var dir = extractedDirs[0];
                if (IsWorldFolder(dir))
                {
                    worldFolderPath = dir;
                    worldName = Path.GetFileName(dir);
                }
            }

            // If not found, check if tempDir itself is a world
            if (worldFolderPath == null && IsWorldFolder(tempDir))
            {
                worldFolderPath = tempDir;
                worldName = "world"; // Default name
            }

            if (worldFolderPath == null)
            {
                throw new ArgumentException("ZIP file does not contain a valid Minecraft world (no level.dat found)");
            }

            // Check if world already exists
            var targetPath = Path.Combine(serverPath, worldName);
            if (Directory.Exists(targetPath))
            {
                throw new ArgumentException($"World '{worldName}' already exists on this server. Use the replace function to update existing worlds.");
            }

            // Move the world folder to the server directory
            if (worldFolderPath == tempDir)
            {
                // tempDir is the world itself
                Directory.Move(tempDir, targetPath);
                tempDir = null; // Prevent cleanup
            }
            else
            {
                // Move the world folder from inside tempDir
                Directory.Move(worldFolderPath, targetPath);
            }

            _logger.LogInformation("Successfully uploaded new world {WorldName}", worldName);
            return worldName;
        }
        finally
        {
            // Clean up temp directory if it still exists
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temporary directory {TempDir}", tempDir);
                }
            }
        }
    }

    public Task DeleteWorldAsync(string serverName, string worldName, CancellationToken cancellationToken)
    {
        var worldPath = GetWorldPath(serverName, worldName);
        if (!Directory.Exists(worldPath))
        {
            throw new FileNotFoundException($"World '{worldName}' not found for server '{serverName}'");
        }

        _logger.LogInformation("Deleting world {WorldName} on server {ServerName}", worldName, serverName);
        Directory.Delete(worldPath, true);
        _logger.LogInformation("Successfully deleted world {WorldName}", worldName);

        return Task.CompletedTask;
    }

    public Task<long> GetWorldSizeAsync(string serverName, string worldName, CancellationToken cancellationToken)
    {
        var worldPath = GetWorldPath(serverName, worldName);
        if (!Directory.Exists(worldPath))
        {
            return Task.FromResult(0L);
        }

        var size = CalculateDirectorySize(worldPath);
        return Task.FromResult(size);
    }

    private static long CalculateDirectorySize(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return 0;
        }

        long size = 0;

        // Add file sizes
        foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
        {
            try
            {
                size += file.Length;
            }
            catch
            {
                // Skip files we can't access
            }
        }

        return size;
    }
}
