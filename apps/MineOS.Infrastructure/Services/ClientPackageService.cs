using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class ClientPackageService : IClientPackageService
{
    private static readonly string[] ClientOverrideFolders =
    {
        "mods",
        "config",
        "defaultconfigs",
        "resourcepacks",
        "shaderpacks",
        "scripts",
        "kubejs"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private const string PackageExtension = ".zip";

    private readonly ILogger<ClientPackageService> _logger;
    private readonly HostOptions _hostOptions;

    public ClientPackageService(
        ILogger<ClientPackageService> logger,
        IOptions<HostOptions> hostOptions)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetClientPackagePath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "client-packages");

    public async Task<IEnumerable<ClientPackageEntryDto>> ListClientPackagesAsync(string serverName, CancellationToken cancellationToken)
    {
        var packagePath = GetClientPackagePath(serverName);
        if (!Directory.Exists(packagePath))
        {
            return Enumerable.Empty<ClientPackageEntryDto>();
        }

        var packages = new List<ClientPackageEntryDto>();
        var files = Directory.GetFiles(packagePath, $"*{PackageExtension}");

        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            packages.Add(new ClientPackageEntryDto(
                fileInfo.LastWriteTimeUtc,
                fileInfo.Length,
                fileInfo.Name
            ));
        }

        return await Task.FromResult(packages.OrderByDescending(p => p.Time));
    }

    public Task<string> CreateClientPackageAsync(string serverName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var metadata = ResolveCurseForgeMetadata(serverPath);

        var packagePath = GetClientPackagePath(serverName);
        Directory.CreateDirectory(packagePath);
        OwnershipHelper.TrySetOwnership(packagePath, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger, recursive: true);

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var packageFilename = $"{serverName}_curseforge_{timestamp}_{suffix}{PackageExtension}";
        var packageFullPath = Path.Combine(packagePath, packageFilename);

        var sourceFolders = ClientOverrideFolders
            .Select(folder => new PackageSource(Path.Combine(serverPath, folder), Path.Combine("overrides", folder)))
            .Where(folder => Directory.Exists(folder.SourcePath))
            .ToList();

        var clientModsPath = Path.Combine(serverPath, "client-mods", "mods");
        if (Directory.Exists(clientModsPath))
        {
            sourceFolders.Add(new PackageSource(clientModsPath, Path.Combine("overrides", "mods")));
        }

        if (sourceFolders.Count == 0)
        {
            throw new InvalidOperationException("No client assets found to package.");
        }

        var manifest = BuildCurseForgeManifest(serverName, metadata);
        var addedFiles = 0;
        var addedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var archive = ZipFile.Open(packageFullPath, ZipArchiveMode.Create);
            AddManifestToArchive(archive, manifest);

            foreach (var folder in sourceFolders)
            {
                addedFiles += AddDirectoryToArchive(
                    archive,
                    folder.SourcePath,
                    folder.EntryRoot,
                    addedEntries,
                    cancellationToken);
            }
        }
        catch
        {
            TryDeleteFile(packageFullPath);
            throw;
        }

        if (addedFiles == 0)
        {
            TryDeleteFile(packageFullPath);
            throw new InvalidOperationException("No client files found to package.");
        }

        OwnershipHelper.TrySetOwnership(packageFullPath, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
        _logger.LogInformation("Created client package {Filename} for server {ServerName}", packageFilename, serverName);

        return Task.FromResult(packageFilename);
    }

    public Task DeleteClientPackageAsync(string serverName, string filename, CancellationToken cancellationToken)
    {
        var packagePath = GetClientPackagePath(serverName);
        var fullPath = Path.Combine(packagePath, filename);

        if (Path.GetFileName(filename) != filename || !filename.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid client package filename");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Client package '{filename}' not found");
        }

        File.Delete(fullPath);
        _logger.LogInformation("Deleted client package {Filename} for server {ServerName}", filename, serverName);

        return Task.CompletedTask;
    }

    public Task<string> GetClientPackagePathAsync(string serverName, string filename, CancellationToken cancellationToken)
    {
        var packagePath = GetClientPackagePath(serverName);
        var fullPath = Path.Combine(packagePath, filename);

        if (Path.GetFileName(filename) != filename || !filename.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid client package filename");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Client package '{filename}' not found");
        }

        return Task.FromResult(fullPath);
    }

    private CurseForgeMetadata ResolveCurseForgeMetadata(string serverPath)
    {
        var modpackMetadata = TryReadModpackManifest(serverPath);
        if (modpackMetadata != null)
        {
            return modpackMetadata;
        }

        if (TryResolveFromServerConfig(serverPath, out var metadata))
        {
            return metadata;
        }

        throw new InvalidOperationException(
            "Unable to determine Minecraft version and mod loader for this server. " +
            "Install a CurseForge modpack or ensure the server jar filename includes the loader and game version.");
    }

    private CurseForgeMetadata? TryReadModpackManifest(string serverPath)
    {
        var modpackPath = Path.Combine(serverPath, "modpacks");
        if (!Directory.Exists(modpackPath))
        {
            return null;
        }

        var modpackFiles = Directory.GetFiles(modpackPath, "*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();

        foreach (var file in modpackFiles)
        {
            try
            {
                using var archive = ZipFile.OpenRead(file.FullName);
                var entry = archive.GetEntry("manifest.json");
                if (entry == null)
                {
                    continue;
                }

                using var stream = entry.Open();
                using var document = JsonDocument.Parse(stream);

                if (!document.RootElement.TryGetProperty("minecraft", out var minecraft))
                {
                    continue;
                }

                if (!minecraft.TryGetProperty("version", out var versionElement))
                {
                    continue;
                }

                var minecraftVersion = versionElement.GetString();
                if (string.IsNullOrWhiteSpace(minecraftVersion))
                {
                    continue;
                }

                string? modLoaderId = null;
                if (minecraft.TryGetProperty("modLoaders", out var modLoaders)
                    && modLoaders.ValueKind == JsonValueKind.Array)
                {
                    foreach (var loader in modLoaders.EnumerateArray())
                    {
                        if (loader.TryGetProperty("id", out var idElement))
                        {
                            modLoaderId = idElement.GetString();
                            if (!string.IsNullOrWhiteSpace(modLoaderId))
                            {
                                break;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(modLoaderId))
                {
                    continue;
                }

                var name = document.RootElement.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var version = document.RootElement.TryGetProperty("version", out var versionProp)
                    ? versionProp.GetString()
                    : null;

                return new CurseForgeMetadata(minecraftVersion, modLoaderId, name, version);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read modpack manifest from {Path}", file.FullName);
            }
        }

        return null;
    }

    private bool TryResolveFromServerConfig(string serverPath, out CurseForgeMetadata metadata)
    {
        metadata = new CurseForgeMetadata(string.Empty, string.Empty, null, null);
        var configPath = Path.Combine(serverPath, "server.config");
        if (!File.Exists(configPath))
        {
            return false;
        }

        var content = File.ReadAllText(configPath);
        var sections = IniParser.ParseWithSections(content);
        if (!sections.TryGetValue("java", out var javaSection))
        {
            return false;
        }

        if (!javaSection.TryGetValue("jarfile", out var jarFile) || string.IsNullOrWhiteSpace(jarFile))
        {
            return false;
        }

        var jarName = Path.GetFileName(jarFile);
        if (TryParseJarMetadata(jarName, out var minecraftVersion, out var modLoaderId))
        {
            metadata = new CurseForgeMetadata(minecraftVersion, modLoaderId, null, null);
            return true;
        }

        return false;
    }

    private static bool TryParseJarMetadata(string jarName, out string minecraftVersion, out string modLoaderId)
    {
        minecraftVersion = string.Empty;
        modLoaderId = string.Empty;

        var patterns = new (Regex Pattern, string LoaderPrefix)[]
        {
            (new Regex(@"^forge-(?<mc>[\d.]+)-(?<loader>[\d.\w-]+?)(?:-(?:server|installer))?\.jar$", RegexOptions.IgnoreCase), "forge"),
            (new Regex(@"^neoforge-(?<mc>[\d.]+)-(?<loader>[\d.\w-]+?)(?:-(?:server|installer))?\.jar$", RegexOptions.IgnoreCase), "neoforge"),
            (new Regex(@"^fabric-server-mc\.(?<mc>[\d.]+)-loader\.(?<loader>[\d.]+).+\.jar$", RegexOptions.IgnoreCase), "fabric"),
            (new Regex(@"^fabric-loader-(?<loader>[\d.]+)-(?<mc>[\d.]+).+\.jar$", RegexOptions.IgnoreCase), "fabric"),
            (new Regex(@"^quilt-server-(?<mc>[\d.]+)-(?<loader>[\d.]+).+\.jar$", RegexOptions.IgnoreCase), "quilt"),
            (new Regex(@"^quilt-loader-(?<loader>[\d.]+)-(?<mc>[\d.]+).+\.jar$", RegexOptions.IgnoreCase), "quilt")
        };

        foreach (var (pattern, prefix) in patterns)
        {
            var match = pattern.Match(jarName);
            if (!match.Success)
            {
                continue;
            }

            minecraftVersion = match.Groups["mc"].Value;
            var loaderVersion = match.Groups["loader"].Value;
            if (string.IsNullOrWhiteSpace(minecraftVersion) || string.IsNullOrWhiteSpace(loaderVersion))
            {
                continue;
            }

            modLoaderId = $"{prefix}-{loaderVersion}";
            return true;
        }

        return false;
    }

    private CurseForgeManifest BuildCurseForgeManifest(string serverName, CurseForgeMetadata metadata)
    {
        var versionTag = metadata.PackVersion;
        if (string.IsNullOrWhiteSpace(versionTag))
        {
            versionTag = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmm");
        }

        var displayName = string.IsNullOrWhiteSpace(metadata.PackName)
            ? $"{serverName} Client Pack"
            : metadata.PackName!;

        return new CurseForgeManifest
        {
            ManifestType = "minecraftModpack",
            ManifestVersion = 1,
            Name = displayName,
            Version = versionTag!,
            Author = "MineOS",
            Files = new List<CurseForgeFileEntry>(),
            Overrides = "overrides",
            Minecraft = new CurseForgeMinecraft
            {
                Version = metadata.MinecraftVersion,
                ModLoaders = new List<CurseForgeModLoader>
                {
                    new(metadata.ModLoaderId, true)
                }
            }
        };
    }

    private static void AddManifestToArchive(ZipArchive archive, CurseForgeManifest manifest)
    {
        var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, manifest, JsonOptions);
    }

    private static int AddDirectoryToArchive(
        ZipArchive archive,
        string sourcePath,
        string entryRoot,
        ISet<string> addedEntries,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
        var added = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var entryName = Path.Combine(entryRoot, relativePath).Replace('\\', '/');
            if (addedEntries.Contains(entryName))
            {
                continue;
            }
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            addedEntries.Add(entryName);
            added++;
        }

        return added;
    }

    private sealed record PackageSource(string SourcePath, string EntryRoot);

    private sealed record CurseForgeMetadata(
        string MinecraftVersion,
        string ModLoaderId,
        string? PackName,
        string? PackVersion);

    private sealed class CurseForgeManifest
    {
        public string ManifestType { get; set; } = string.Empty;
        public int ManifestVersion { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public List<CurseForgeFileEntry> Files { get; set; } = new();
        public string Overrides { get; set; } = "overrides";
        public CurseForgeMinecraft Minecraft { get; set; } = new();
    }

    private sealed record CurseForgeFileEntry(int ProjectId, int FileId, bool Required);

    private sealed class CurseForgeMinecraft
    {
        public string Version { get; set; } = string.Empty;
        public List<CurseForgeModLoader> ModLoaders { get; set; } = new();
    }

    private sealed record CurseForgeModLoader(string Id, bool Primary);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
