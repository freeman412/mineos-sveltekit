using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class ModService : IModService
{
    private const string RestartFlagFile = ".mineos-restart-required";
    private static readonly TimeSpan[] DownloadRetryDelays = new[]
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly HashSet<string> ClientOnlyCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "client-side",
        "shader",
        "shaders",
        "rendering",
        "ui",
        "gui",
        "cit",
        "cosmetic",
        "capes"
    };

    private readonly ILogger<ModService> _logger;
    private readonly HostOptions _hostOptions;
    private readonly ICurseForgeService _curseForgeService;
    private readonly IModrinthService _modrinthService;
    private readonly ISettingsService _settingsService;
    private readonly IServerService _serverService;
    private readonly IProfileService _profileService;
    private readonly HttpClient _httpClient;
    private readonly IModpackRepository _modpackRepo;

    public ModService(
        ILogger<ModService> logger,
        IOptions<HostOptions> hostOptions,
        ICurseForgeService curseForgeService,
        IModrinthService modrinthService,
        ISettingsService settingsService,
        IServerService serverService,
        IProfileService profileService,
        HttpClient httpClient,
        IModpackRepository modpackRepo)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
        _curseForgeService = curseForgeService;
        _modrinthService = modrinthService;
        _settingsService = settingsService;
        _serverService = serverService;
        _profileService = profileService;
        _httpClient = httpClient;
        _modpackRepo = modpackRepo;
    }

    public Task<IReadOnlyList<InstalledModDto>> ListModsAsync(string serverName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var modsPath = GetModsPath(serverName);
        if (!Directory.Exists(modsPath))
        {
            return Task.FromResult<IReadOnlyList<InstalledModDto>>(Array.Empty<InstalledModDto>());
        }

        var mods = Directory.GetFiles(modsPath)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var fileName = info.Name;
                return new InstalledModDto(
                    fileName,
                    info.Length,
                    info.LastWriteTimeUtc,
                    fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(m => m.FileName)
            .ToList();

        return Task.FromResult<IReadOnlyList<InstalledModDto>>(mods);
    }

    public async Task SaveModAsync(string serverName, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var safeName = ValidateFileName(fileName);
        var modsPath = EnsureModsPath(serverName);
        var lowerName = safeName.ToLowerInvariant();

        // Check if this is an archive file that needs extraction
        var isZip = lowerName.EndsWith(".zip");
        var isTar = lowerName.EndsWith(".tar") || lowerName.EndsWith(".tar.gz") || lowerName.EndsWith(".tgz");

        if (isZip || isTar)
        {
            // Save archive to a temporary location
            var tempPath = Path.Combine(Path.GetTempPath(), $"mineos_upload_{Guid.NewGuid():N}{Path.GetExtension(safeName)}");
            try
            {
                await using (var tempFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await content.CopyToAsync(tempFile, cancellationToken);
                }

                // Extract archive contents
                if (isZip)
                {
                    await ExtractZipToModsAsync(tempPath, modsPath, cancellationToken);
                }
                else if (isTar)
                {
                    await ExtractTarToModsAsync(tempPath, modsPath, cancellationToken);
                }

                _logger.LogInformation("Extracted archive {FileName} for server {ServerName}", safeName, serverName);
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        else
        {
            // Regular file (JAR) - save directly
            var targetPath = Path.Combine(modsPath, safeName);
            await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await content.CopyToAsync(target, cancellationToken);
            await OwnershipHelper.ChangeOwnershipAsync(
                targetPath,
                _hostOptions.RunAsUid,
                _hostOptions.RunAsGid,
                _logger,
                cancellationToken);
            _logger.LogInformation("Uploaded mod {FileName} for server {ServerName}", safeName, serverName);
        }

        MarkRestartRequired(serverPath);
    }

    public Task DeleteModAsync(string serverName, string fileName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var safeName = ValidateFileName(fileName);
        var modsPath = GetModsPath(serverName);
        var targetPath = Path.Combine(modsPath, safeName);

        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException($"Mod '{safeName}' not found");
        }

        File.Delete(targetPath);
        MarkRestartRequired(serverPath);
        _logger.LogInformation("Deleted mod {FileName} for server {ServerName}", safeName, serverName);
        return Task.CompletedTask;
    }

    public Task<string> GetModPathAsync(string serverName, string fileName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var safeName = ValidateFileName(fileName);
        var modsPath = GetModsPath(serverName);
        var targetPath = Path.Combine(modsPath, safeName);

        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException($"Mod '{safeName}' not found");
        }

        return Task.FromResult(targetPath);
    }

    public async Task InstallModFromCurseForgeAsync(
        string serverName,
        int modId,
        int? fileId,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var resolvedFileId = await ResolveFileIdAsync(modId, fileId, cancellationToken);
        var modFile = await _curseForgeService.GetModFileAsync(modId, resolvedFileId, cancellationToken);
        var serverVersion = await GetServerMinecraftVersionAsync(serverName, cancellationToken);
        if (!string.IsNullOrWhiteSpace(serverVersion) && modFile.GameVersions.Count > 0 &&
            !IsMinecraftVersionMatch(serverVersion, modFile.GameVersions))
        {
            var versionList = FormatVersionList(modFile.GameVersions);
            progress.Report(new JobProgressDto(
                string.Empty,
                "mod-install",
                serverName,
                "running",
                0,
                $"Warning: Mod targets Minecraft {versionList} but server is {serverVersion}.",
                DateTimeOffset.UtcNow));
        }
        var downloadUrl = modFile.DownloadUrl ??
                          await _curseForgeService.GetModFileDownloadUrlAsync(modId, resolvedFileId, cancellationToken);

        var modsPath = EnsureModsPath(serverName);
        var targetPath = Path.Combine(modsPath, ValidateFileName(modFile.FileName));

        await DownloadFileAsync(downloadUrl, targetPath, progress, serverName, "mod-install", cancellationToken);
        MarkRestartRequired(GetServerPath(serverName));
        _logger.LogInformation("Installed mod {ModId} ({FileName}) for server {ServerName}", modId, modFile.FileName, serverName);
    }

    public async Task InstallModpackAsync(
        string serverName,
        int modpackId,
        int? fileId,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var resolvedFileId = await ResolveFileIdAsync(modpackId, fileId, cancellationToken);
        var modpackFile = await _curseForgeService.GetModFileAsync(modpackId, resolvedFileId, cancellationToken);
        var serverPackFile = modpackFile.IsServerPack == true
            ? modpackFile
            : await ResolveServerPackFileAsync(modpackId, modpackFile, cancellationToken);
        var selectedFile = serverPackFile ?? modpackFile;

        if (serverPackFile != null)
        {
            progress.Report(new JobProgressDto(
                string.Empty,
                "modpack-install",
                serverName,
                "running",
                0,
                $"Using CurseForge server pack {serverPackFile.FileName}",
                DateTimeOffset.UtcNow));
        }

        var downloadUrl = selectedFile.DownloadUrl ??
                          await _curseForgeService.GetModFileDownloadUrlAsync(modpackId, selectedFile.Id, cancellationToken);

        var modpackPath = Path.Combine(EnsureModpackPath(serverName), ValidateFileName(selectedFile.FileName));

        progress.Report(new JobProgressDto(string.Empty, "modpack-install", serverName, "running", 0, "Downloading modpack", DateTimeOffset.UtcNow));
        await DownloadFileAsync(downloadUrl, modpackPath, progress, serverName, "modpack-install", cancellationToken);

        await ApplyModpackAsync(serverName, modpackPath, serverPackFile != null, progress, cancellationToken);
        MarkRestartRequired(GetServerPath(serverName));
        _logger.LogInformation("Installed modpack {ModpackId} ({FileName}) for server {ServerName}", modpackId, selectedFile.FileName, serverName);
    }

    public async Task InstallModrinthModpackAsync(
        string serverName,
        string projectId,
        string versionId,
        string? projectName,
        string? projectVersion,
        string? logoUrl,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var version = await _modrinthService.GetVersionAsync(versionId, cancellationToken);
        if (version == null)
        {
            throw new InvalidOperationException("Modrinth version not found");
        }

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
        if (file == null)
        {
            throw new InvalidOperationException("Modrinth version has no downloadable files");
        }

        var modpackFileName = ValidateFileName(file.FileName);
        var modpackPath = Path.Combine(EnsureModpackPath(serverName), modpackFileName);

        progress.Report(new JobProgressDto(string.Empty, "modrinth-modpack-install", serverName, "running", 0, "Downloading modpack", DateTimeOffset.UtcNow));
        await DownloadFileAsync(file.Url, modpackPath, progress, serverName, "modrinth-modpack-install", cancellationToken);

        await ApplyModrinthModpackAsync(
            serverName,
            modpackPath,
            projectId,
            projectName ?? projectId,
            projectVersion ?? version.Name ?? version.VersionNumber,
            logoUrl,
            progress,
            cancellationToken);

        MarkRestartRequired(serverPath);
        _logger.LogInformation("Installed Modrinth modpack {ProjectId} ({VersionId}) for server {ServerName}",
            projectId, versionId, serverName);
    }

    public async Task<IReadOnlyList<InstalledModWithModpackDto>> ListModsWithModpacksAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var modsPath = GetModsPath(serverName);
        if (!Directory.Exists(modsPath))
        {
            return Array.Empty<InstalledModWithModpackDto>();
        }

        // Get all mod files from disk
        var modFiles = Directory.GetFiles(modsPath)
            .Select(path => new FileInfo(path))
            .ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

        // Get installed mod records from database
        Dictionary<string, InstalledModRecord> installedModsByFileName;
        try
        {
            var installedMods = await _modpackRepo.GetModsByServerWithModpackAsync(serverName, cancellationToken);

            installedModsByFileName = installedMods.ToDictionary(
                m => m.FileName,
                m => m,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            _logger.LogWarning("InstalledModRecords table does not exist yet. Run database migrations.");
            installedModsByFileName = new Dictionary<string, InstalledModRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new List<InstalledModWithModpackDto>();

        foreach (var (fileName, fileInfo) in modFiles)
        {
            var isDisabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

            if (installedModsByFileName.TryGetValue(fileName, out var record))
            {
                result.Add(new InstalledModWithModpackDto(
                    fileName,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc,
                    isDisabled,
                    record.ModpackId,
                    record.Modpack?.Name,
                    record.CurseForgeProjectId));
            }
            else
            {
                result.Add(new InstalledModWithModpackDto(
                    fileName,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc,
                    isDisabled,
                    null,
                    null,
                    null));
            }
        }

        return result.OrderBy(m => m.FileName).ToList();
    }

    public async Task InstallModpackWithStateAsync(
        string serverName,
        int modpackId,
        int? fileId,
        string modpackName,
        string? modpackVersion,
        string? logoUrl,
        IModpackInstallState state,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        state.AppendOutput($"Resolving modpack file for {modpackName}...");
        var resolvedFileId = await ResolveFileIdAsync(modpackId, fileId, cancellationToken);
        var modpackFile = await _curseForgeService.GetModFileAsync(modpackId, resolvedFileId, cancellationToken);
        var serverPackFile = modpackFile.IsServerPack == true
            ? modpackFile
            : await ResolveServerPackFileAsync(modpackId, modpackFile, cancellationToken);
        var selectedFile = serverPackFile ?? modpackFile;
        var downloadUrl = selectedFile.DownloadUrl ??
                          await _curseForgeService.GetModFileDownloadUrlAsync(modpackId, selectedFile.Id, cancellationToken);

        var modpackPath = Path.Combine(EnsureModpackPath(serverName), ValidateFileName(selectedFile.FileName));

        state.UpdateProgress(5, "Downloading modpack archive");
        if (serverPackFile != null)
        {
            state.AppendOutput($"Using CurseForge server pack {serverPackFile.FileName}");
        }
        state.AppendOutput($"Downloading {selectedFile.FileName}...");
        await DownloadFileWithStateAsync(downloadUrl, modpackPath, state, cancellationToken);

        try
        {
            await ApplyModpackWithStateAsync(
                serverName,
                modpackPath,
                modpackId,
                modpackName,
                modpackVersion,
                logoUrl,
                serverPackFile != null,
                state,
                cancellationToken);
            MarkRestartRequired(serverPath);
            _logger.LogInformation("Installed modpack {ModpackId} ({FileName}) for server {ServerName}", modpackId, selectedFile.FileName, serverName);
        }
        catch (Exception ex)
        {
            state.AppendOutput($"ERROR: {ex.Message}");
            state.AppendOutput("Rolling back installation...");

            var installedFiles = state.GetInstalledFilePaths();
            foreach (var filePath in installedFiles)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        state.AppendOutput($"Removed: {Path.GetFileName(filePath)}");
                    }
                }
                catch (Exception deleteEx)
                {
                    state.AppendOutput($"Failed to remove {Path.GetFileName(filePath)}: {deleteEx.Message}");
                    _logger.LogWarning(deleteEx, "Failed to remove file during rollback: {FilePath}", filePath);
                }
            }

            state.AppendOutput($"Rollback complete. Removed {installedFiles.Count} files.");
            throw;
        }
    }

    public async Task<IReadOnlyList<InstalledModpackDto>> ListInstalledModpacksAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        try
        {
            var modpacks = await _modpackRepo.GetModpacksByServerAsync(serverName, cancellationToken);

            // Order on client side (SQLite doesn't support DateTimeOffset in ORDER BY)
            return modpacks
                .OrderByDescending(m => m.InstalledAt)
                .Select(m => new InstalledModpackDto(
                    m.Id,
                    m.Name,
                    m.Version,
                    m.LogoUrl,
                    m.ModCount,
                    m.InstalledAt))
                .ToList();
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1) // SQLITE_ERROR - table doesn't exist
        {
            _logger.LogWarning("InstalledModpacks table does not exist yet. Run database migrations.");
            return Array.Empty<InstalledModpackDto>();
        }
    }

    public async Task UninstallModpackAsync(
        string serverName,
        int modpackDbId,
        CancellationToken cancellationToken)
    {
        var modpack = await _modpackRepo.GetModpackWithModsAsync(modpackDbId, serverName, cancellationToken);

        if (modpack == null)
        {
            throw new InvalidOperationException($"Modpack with ID {modpackDbId} not found");
        }

        var modsPath = GetModsPath(serverName);
        var deletedCount = 0;

        foreach (var modRecord in modpack.Mods)
        {
            var modPath = Path.Combine(modsPath, modRecord.FileName);
            if (File.Exists(modPath))
            {
                File.Delete(modPath);
                deletedCount++;
                _logger.LogDebug("Deleted mod file {FileName} from modpack {ModpackName}", modRecord.FileName, modpack.Name);
            }
        }

        await _modpackRepo.RemoveModpackAsync(modpack, cancellationToken);

        MarkRestartRequired(GetServerPath(serverName));
        _logger.LogInformation("Uninstalled modpack {ModpackName} ({ModpackId}), removed {Count} mod files",
            modpack.Name, modpackDbId, deletedCount);
    }

    private async Task ApplyModpackWithStateAsync(
        string serverName,
        string modpackPath,
        int curseForgeProjectId,
        string modpackName,
        string? modpackVersion,
        string? logoUrl,
        bool isServerPack,
        IModpackInstallState state,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(modpackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        if (isServerPack)
        {
            await ApplyCurseForgeServerPackWithStateAsync(
                serverName,
                archive,
                curseForgeProjectId,
                modpackName,
                modpackVersion,
                logoUrl,
                state,
                cancellationToken);
            return;
        }

        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry == null)
        {
            throw new InvalidOperationException("Modpack manifest.json not found");
        }

        ModpackManifest? manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<ModpackManifest>(manifestStream, JsonOptions, cancellationToken);
        }

        if (manifest == null || manifest.Files.Count == 0)
        {
            throw new InvalidOperationException("Modpack manifest is missing required files");
        }

        var manifestVersion = manifest.Minecraft?.Version;
        if (!string.IsNullOrWhiteSpace(manifestVersion))
        {
            var serverVersion = await GetServerMinecraftVersionAsync(serverName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(serverVersion) &&
                !IsMinecraftVersionMatch(serverVersion, new[] { manifestVersion }))
            {
                state.AppendOutput($"WARNING: Modpack targets Minecraft {manifestVersion} but server is {serverVersion}.");
            }
        }

        state.SetTotalMods(manifest.Files.Count);
        state.AppendOutput($"Found {manifest.Files.Count} mods to install");

        state.UpdateProgress(10, "Extracting overrides");
        state.AppendOutput("Extracting override files...");
        var extractedOverrides = ExtractOverridesWithTracking(archive, serverName, state);
        state.AppendOutput($"Extracted {extractedOverrides} override files");
        var overrideModFiles = GetOverrideModFileNames(archive, "overrides/");

        state.UpdateProgress(15, "Resolving modpack files");
        var downloads = await ResolveModpackDownloadsAsync(
            manifest.Files,
            cancellationToken,
            state.AppendOutput);
        state.SetTotalMods(downloads.Count);
        state.AppendOutput($"Resolved {downloads.Count} mod files");

        var installedModRecords = new List<InstalledModRecord>();

        for (var i = 0; i < downloads.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var download = downloads[i];
            state.UpdateModProgress(i + 1, download.FileName);

            try
            {
                state.AppendOutput($"Downloading: {download.FileName}");
                state.AppendOutput($"  URL: {download.DownloadUrl}");
                _logger.LogInformation("Downloading mod {ModId}/{FileId} ({FileName}) from {Url}",
                    download.ProjectId, download.FileId, download.FileName, download.DownloadUrl);

                var modsPath = EnsureModsPath(serverName);
                var targetPath = Path.Combine(modsPath, ValidateFileName(download.FileName));

                await DownloadFileWithStateAsync(download.DownloadUrl, targetPath, state, cancellationToken);
                state.RecordInstalledFile(targetPath);

                installedModRecords.Add(new InstalledModRecord
                {
                    ServerName = serverName,
                    FileName = download.FileName,
                    CurseForgeProjectId = download.ProjectId,
                    ModName = null, // Could fetch mod name if needed
                    InstalledAt = DateTimeOffset.UtcNow
                });

                state.AppendOutput($"Installed: {download.FileName}");
            }
            catch (Exception ex)
            {
                state.AppendOutput($"ERROR downloading mod {download.ProjectId}: {ex.Message}");
                throw;
            }
        }

        state.UpdateProgress(95, "Saving modpack records");
        state.AppendOutput("Saving installation records to database...");

        AddOverrideModRecords(installedModRecords, overrideModFiles, serverName);

        await SaveCurseForgeModpackRecordsAsync(
            serverName,
            curseForgeProjectId,
            modpackName,
            modpackVersion,
            logoUrl,
            installedModRecords,
            cancellationToken);
        state.AppendOutput($"Installation complete! Installed {installedModRecords.Count} mods.");
    }

    private async Task ApplyCurseForgeServerPackWithStateAsync(
        string serverName,
        ZipArchive archive,
        int curseForgeProjectId,
        string modpackName,
        string? modpackVersion,
        string? logoUrl,
        IModpackInstallState state,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        var rootPrefix = GetArchiveRootPrefix(archive);
        var modFiles = GetArchiveModFileNames(archive, rootPrefix);
        var installedModRecords = new List<InstalledModRecord>();
        var extractedCount = 0;
        var modIndex = 0;

        state.SetTotalMods(modFiles.Count);
        state.UpdateProgress(10, "Extracting server pack");
        state.AppendOutput("Extracting server pack files...");

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = TrimArchiveRoot(entry.FullName, rootPrefix);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var destination = GetSafePath(serverPath, relativePath);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                OwnershipHelper.TrySetOwnership(directory, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
            }

            using var entryStream = entry.Open();
            using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await entryStream.CopyToAsync(fileStream, cancellationToken);
            OwnershipHelper.TrySetOwnership(destination, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
            state.RecordInstalledFile(destination);
            extractedCount++;

            if (IsModPath(relativePath) && IsJarFile(relativePath))
            {
                modIndex++;
                state.UpdateModProgress(modIndex, Path.GetFileName(relativePath));
            }
        }

        state.AppendOutput($"Extracted {extractedCount} file(s).");

        AddOverrideModRecords(installedModRecords, modFiles, serverName);

        state.UpdateProgress(95, "Saving modpack records");
        state.AppendOutput("Saving installation records to database...");

        await SaveCurseForgeModpackRecordsAsync(
            serverName,
            curseForgeProjectId,
            modpackName,
            modpackVersion,
            logoUrl,
            installedModRecords,
            cancellationToken);

        state.AppendOutput($"Installation complete! Installed {installedModRecords.Count} mods.");
    }

    private async Task SaveCurseForgeModpackRecordsAsync(
        string serverName,
        int curseForgeProjectId,
        string modpackName,
        string? modpackVersion,
        string? logoUrl,
        List<InstalledModRecord> installedModRecords,
        CancellationToken cancellationToken)
    {
        await _modpackRepo.UpsertModpackAsync(
            serverName, "curseforge", curseForgeProjectId.ToString(),
            modpackName, modpackVersion, logoUrl, curseForgeProjectId,
            installedModRecords, cancellationToken);
    }

    private int ExtractOverridesWithTracking(ZipArchive archive, string serverName, IModpackInstallState state)
    {
        var serverPath = GetServerPath(serverName);
        var count = 0;

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(entry.FullName, "overrides/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = entry.FullName.Substring("overrides/".Length);
            var destination = GetSafePath(serverPath, relativePath);

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                OwnershipHelper.TrySetOwnership(destination, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
                continue;
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                OwnershipHelper.TrySetOwnership(directory, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
            }

            using var entryStream = entry.Open();
            using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(fileStream);
            OwnershipHelper.TrySetOwnership(destination, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);

            state.RecordInstalledFile(destination);
            count++;
        }

        return count;
    }

    private int ExtractModsFromArchive(ZipArchive archive, string serverName, IModpackInstallState state)
    {
        var modsPath = EnsureModsPath(serverName);
        var count = 0;

        foreach (var entry in archive.Entries)
        {
            // Look for mod files in overrides/mods/ or mods/ folders
            var isInMods = entry.FullName.StartsWith("overrides/mods/", StringComparison.OrdinalIgnoreCase) ||
                           entry.FullName.StartsWith("mods/", StringComparison.OrdinalIgnoreCase);

            if (!isInMods)
            {
                continue;
            }

            // Skip directory entries
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            // Only extract .jar files
            if (!entry.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(modsPath, entry.Name);
            using var entryStream = entry.Open();
            using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(fileStream);
            OwnershipHelper.TrySetOwnership(destination, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);

            state.RecordInstalledFile(destination);
            state.AppendOutput($"Extracted mod: {entry.Name}");
            count++;
        }

        return count;
    }

    private async Task SaveModpackRecordsAsync(
        string serverName,
        int curseForgeProjectId,
        string modpackName,
        string? modpackVersion,
        string? logoUrl,
        List<InstalledModRecord> modRecords,
        CancellationToken cancellationToken)
    {
        await _modpackRepo.AddModpackAsync(
            serverName, "curseforge", curseForgeProjectId.ToString(),
            modpackName, modpackVersion, logoUrl, curseForgeProjectId,
            modRecords, cancellationToken);
    }

    private async Task DownloadFileWithStateAsync(
        string url,
        string targetPath,
        IModpackInstallState state,
        CancellationToken cancellationToken)
    {
        await DownloadFileWithRetriesAsync(
            url,
            targetPath,
            cancellationToken,
            null,
            message => state.AppendOutput(message));

        await OwnershipHelper.ChangeOwnershipAsync(
            targetPath,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);
    }

    private async Task ApplyModpackAsync(
        string serverName,
        string modpackPath,
        bool isServerPack,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(modpackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        if (isServerPack)
        {
            await ApplyCurseForgeServerPackAsync(serverName, archive, progress, cancellationToken);
            return;
        }

        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry == null)
        {
            throw new InvalidOperationException("Modpack manifest.json not found");
        }

        ModpackManifest? manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<ModpackManifest>(manifestStream, JsonOptions, cancellationToken);
        }

        if (manifest == null || manifest.Files.Count == 0)
        {
            throw new InvalidOperationException("Modpack manifest is missing required files");
        }

        var manifestVersion = manifest.Minecraft?.Version;
        if (!string.IsNullOrWhiteSpace(manifestVersion))
        {
            var serverVersion = await GetServerMinecraftVersionAsync(serverName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(serverVersion) &&
                !IsMinecraftVersionMatch(serverVersion, new[] { manifestVersion }))
            {
                progress.Report(new JobProgressDto(
                    string.Empty,
                    "modpack-install",
                    serverName,
                    "running",
                    0,
                    $"Warning: Modpack targets Minecraft {manifestVersion} but server is {serverVersion}.",
                    DateTimeOffset.UtcNow));
            }
        }

        var total = manifest.Files.Count + 1;
        var completed = 0;

        progress.Report(new JobProgressDto(string.Empty, "modpack-install", serverName, "running", 0, "Extracting overrides", DateTimeOffset.UtcNow));
        ExtractOverrides(archive, serverName, "overrides/");
        completed++;
        ReportProgress(progress, serverName, "modpack-install", completed, total, "Overrides extracted");

        ReportProgress(progress, serverName, "modpack-install", completed, total, "Resolving modpack files");
        var downloads = await ResolveModpackDownloadsAsync(
            manifest.Files,
            cancellationToken,
            message => ReportProgress(progress, serverName, "modpack-install", completed, total, message));
        total = downloads.Count + 1;

        foreach (var download in downloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stepMessage = $"Downloading mod {completed}/{total - 1}";
            ReportProgress(progress, serverName, "modpack-install", completed, total, stepMessage);
            var modsPath = EnsureModsPath(serverName);
            var targetPath = Path.Combine(modsPath, ValidateFileName(download.FileName));

            await DownloadFileWithRetriesAsync(
                download.DownloadUrl,
                targetPath,
                cancellationToken,
                null,
                message => ReportProgress(progress, serverName, "modpack-install", completed, total, message));

            await OwnershipHelper.ChangeOwnershipAsync(
                targetPath,
                _hostOptions.RunAsUid,
                _hostOptions.RunAsGid,
                _logger,
                cancellationToken);
            completed++;
            ReportProgress(progress, serverName, "modpack-install", completed, total, $"Downloaded {download.FileName}");
        }
    }

    private async Task ApplyCurseForgeServerPackAsync(
        string serverName,
        ZipArchive archive,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        var rootPrefix = GetArchiveRootPrefix(archive);
        var fileEntries = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToList();
        var total = fileEntries.Count;
        var completed = 0;

        progress.Report(new JobProgressDto(
            string.Empty,
            "modpack-install",
            serverName,
            "running",
            0,
            "Extracting server pack",
            DateTimeOffset.UtcNow));

        foreach (var entry in fileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = TrimArchiveRoot(entry.FullName, rootPrefix);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var destination = GetSafePath(serverPath, relativePath);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                OwnershipHelper.TrySetOwnership(directory, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
            }

            using var entryStream = entry.Open();
            await using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await entryStream.CopyToAsync(fileStream, cancellationToken);
            OwnershipHelper.TrySetOwnership(destination, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);

            completed++;
            if (completed % 25 == 0 || completed == total)
            {
                ReportProgress(
                    progress,
                    serverName,
                    "modpack-install",
                    completed,
                    total,
                    $"Extracted {completed}/{total} files");
            }
        }
    }

    private async Task ApplyModrinthModpackAsync(
        string serverName,
        string modpackPath,
        string projectId,
        string modpackName,
        string? modpackVersion,
        string? logoUrl,
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(modpackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        var manifestEntry = archive.GetEntry("modrinth.index.json");
        if (manifestEntry == null)
        {
            throw new InvalidOperationException("Modrinth modpack index not found");
        }

        ModrinthModpackIndex? manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<ModrinthModpackIndex>(manifestStream, JsonOptions, cancellationToken);
        }

        if (manifest == null || manifest.Files.Count == 0)
        {
            throw new InvalidOperationException("Modrinth modpack is missing required files");
        }

        if (manifest.Dependencies.TryGetValue("minecraft", out var minecraftVersion) &&
            !string.IsNullOrWhiteSpace(minecraftVersion))
        {
            var serverVersion = await GetServerMinecraftVersionAsync(serverName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(serverVersion) &&
                !IsMinecraftVersionMatch(serverVersion, new[] { minecraftVersion }))
            {
                progress.Report(new JobProgressDto(
                    string.Empty,
                    "modrinth-modpack-install",
                    serverName,
                    "running",
                    0,
                    $"Warning: Modpack targets Minecraft {minecraftVersion} but server is {serverVersion}.",
                    DateTimeOffset.UtcNow));
            }
        }

        var serverPath = GetServerPath(serverName);
        var total = manifest.Files.Count + 1;
        var completed = 0;

        progress.Report(new JobProgressDto(string.Empty, "modrinth-modpack-install", serverName, "running", 0, "Extracting overrides", DateTimeOffset.UtcNow));
        ExtractOverrides(archive, serverName, "overrides/", "server-overrides/", "server_overrides/");
        completed++;
        ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, "Overrides extracted");
        var overrideModFiles = GetOverrideModFileNames(archive, "overrides/", "server-overrides/", "server_overrides/");

        var installedRecords = new List<InstalledModRecord>();
        var projectCache = new Dictionary<string, ModrinthProjectDto?>(StringComparer.OrdinalIgnoreCase);
        var versionCache = new Dictionary<string, ModrinthVersionDto?>(StringComparer.OrdinalIgnoreCase);
        var decisions = new List<ModrinthFileDecision>();

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = await EvaluateModrinthModFileAsync(file, projectCache, versionCache, cancellationToken);
            decisions.Add(decision);
        }

        var installFiles = decisions.Where(d => d.ShouldInstall).ToList();
        var excludedMods = decisions
            .Where(d => !d.ShouldInstall && IsModPath(d.File.Path))
            .ToList();
        var missingDownloads = decisions
            .Where(d => string.IsNullOrWhiteSpace(d.File.Downloads.FirstOrDefault()))
            .Select(d => d with { ShouldInstall = false, Reason = "MISSING_DOWNLOAD_URL" })
            .ToList();

        if (missingDownloads.Count > 0)
        {
            var reportExclusions = excludedMods
                .Concat(missingDownloads.Where(d => IsModPath(d.File.Path)))
                .Distinct()
                .ToList();

            var missingDownloadReportPath = await WriteModrinthInstallReportAsync(
                serverName,
                modpackName,
                modpackVersion,
                0,
                reportExclusions,
                0,
                Array.Empty<string>(),
                cancellationToken);

            throw new InvalidOperationException(
                $"Modrinth modpack is missing download URLs for {missingDownloads.Count} file(s). See {Path.GetFileName(missingDownloadReportPath)}.");
        }

        foreach (var excluded in excludedMods)
        {
            _logger.LogWarning(
                "Excluded Modrinth mod {FilePath} for server {ServerName} ({Reason})",
                excluded.File.Path,
                serverName,
                excluded.Reason ?? "UNKNOWN");
        }

        var loader = await ResolveModrinthLoaderAsync(manifest, serverName, cancellationToken);
        var dependencyResolution = await ResolveModrinthDependenciesAsync(
            installFiles.Where(d => IsModPath(d.File.Path)).ToList(),
            excludedMods,
            loader,
            minecraftVersion ?? await GetServerMinecraftVersionAsync(serverName, cancellationToken),
            projectCache,
            versionCache,
            cancellationToken);

        if (dependencyResolution.Errors.Count > 0)
        {
            var missingDependencyReportPath = await WriteModrinthInstallReportAsync(
                serverName,
                modpackName,
                modpackVersion,
                0,
                excludedMods,
                0,
                dependencyResolution.Errors,
                cancellationToken);

            throw new InvalidOperationException(
                $"Missing required Modrinth dependencies: {string.Join(", ", dependencyResolution.Errors)}. See {Path.GetFileName(missingDependencyReportPath)}.");
        }

        total = installFiles.Count + excludedMods.Count + dependencyResolution.Installs.Count + 1;

        foreach (var decision in installFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = decision.File;
            var downloadUrl = file.Downloads.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new InvalidOperationException(
                    $"Missing download URL for {Path.GetFileName(file.Path)}.");
            }

            var targetPath = GetSafePath(serverPath, file.Path);
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stepMessage = $"Downloading {Path.GetFileName(file.Path)}";
            ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, stepMessage);

            await DownloadFileWithRetriesAsync(
                downloadUrl,
                targetPath,
                cancellationToken,
                null,
                message => ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, message));

            await OwnershipHelper.ChangeOwnershipAsync(
                targetPath,
                _hostOptions.RunAsUid,
                _hostOptions.RunAsGid,
                _logger,
                cancellationToken);

            if (IsModPath(file.Path))
            {
                var fileName = Path.GetFileName(file.Path);
                installedRecords.Add(new InstalledModRecord
                {
                    ServerName = serverName,
                    FileName = fileName,
                    CurseForgeProjectId = null,
                    ModName = decision.ProjectName ?? Path.GetFileNameWithoutExtension(fileName),
                    InstalledAt = DateTimeOffset.UtcNow
                });
            }

            completed++;
            ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, $"Downloaded {Path.GetFileName(file.Path)}");
        }

        if (excludedMods.Count > 0)
        {
            var clientModsRoot = Path.Combine(serverPath, "client-mods");
            Directory.CreateDirectory(clientModsRoot);
            OwnershipHelper.TrySetOwnership(clientModsRoot, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger, recursive: true);

            foreach (var excluded in excludedMods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = excluded.File;
                var downloadUrl = file.Downloads.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    throw new InvalidOperationException(
                        $"Missing download URL for {Path.GetFileName(file.Path)}.");
                }

                var targetPath = GetSafePath(clientModsRoot, file.Path);
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var stepMessage = $"Saving client-only {Path.GetFileName(file.Path)}";
                ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, stepMessage);

                await DownloadFileWithRetriesAsync(
                    downloadUrl,
                    targetPath,
                    cancellationToken,
                    null,
                    message => ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, message));

                await OwnershipHelper.ChangeOwnershipAsync(
                    targetPath,
                    _hostOptions.RunAsUid,
                    _hostOptions.RunAsGid,
                    _logger,
                    cancellationToken);

                completed++;
                ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, $"Saved client-only {Path.GetFileName(file.Path)}");
            }
        }

        if (dependencyResolution.Installs.Count > 0)
        {
            var modsPath = EnsureModsPath(serverName);
            foreach (var dependency in dependencyResolution.Installs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = SelectModrinthFile(dependency.Version);
                if (file == null || string.IsNullOrWhiteSpace(file.Url))
                {
                    throw new InvalidOperationException(
                        $"Missing download URL for dependency {dependency.ProjectName ?? dependency.ProjectId}.");
                }

                var targetPath = Path.Combine(modsPath, ValidateFileName(file.FileName));
                var stepMessage = $"Downloading dependency {Path.GetFileName(file.FileName)}";
                ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, stepMessage);

                await DownloadFileWithRetriesAsync(
                    file.Url,
                    targetPath,
                    cancellationToken,
                    null,
                    message => ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, message));

                await OwnershipHelper.ChangeOwnershipAsync(
                    targetPath,
                    _hostOptions.RunAsUid,
                    _hostOptions.RunAsGid,
                    _logger,
                    cancellationToken);

                installedRecords.Add(new InstalledModRecord
                {
                    ServerName = serverName,
                    FileName = Path.GetFileName(targetPath),
                    CurseForgeProjectId = null,
                    ModName = dependency.ProjectName ?? Path.GetFileNameWithoutExtension(targetPath),
                    InstalledAt = DateTimeOffset.UtcNow
                });

                completed++;
                ReportProgress(progress, serverName, "modrinth-modpack-install", completed, total, $"Downloaded dependency {Path.GetFileName(targetPath)}");
            }
        }

        AddOverrideModRecords(installedRecords, overrideModFiles, serverName);

        await _modpackRepo.UpsertModpackAsync(
            serverName, "modrinth", projectId,
            modpackName, modpackVersion, logoUrl, null,
            installedRecords, cancellationToken);

        var reportPath = await WriteModrinthInstallReportAsync(
            serverName,
            modpackName,
            modpackVersion,
            installedRecords.Count,
            excludedMods,
            dependencyResolution.Installs.Count,
            dependencyResolution.Errors,
            cancellationToken);

        progress.Report(new JobProgressDto(
            string.Empty,
            "modrinth-modpack-install",
            serverName,
            "running",
            100,
            $"Installed {installedRecords.Count} mod(s), excluded {excludedMods.Count} mod(s), missing {dependencyResolution.Errors.Count} dependency(ies). Report saved to {Path.GetFileName(reportPath)}.",
            DateTimeOffset.UtcNow));
    }

    private void ExtractOverrides(ZipArchive archive, string serverName, params string[] roots)
    {
        var serverPath = GetServerPath(serverName);
        var normalizedRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => root.EndsWith("/", StringComparison.Ordinal) ? root : $"{root}/")
            .ToList();

        foreach (var entry in archive.Entries)
        {
            string? matchedRoot = null;
            foreach (var root in normalizedRoots)
            {
                if (entry.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    matchedRoot = root;
                    break;
                }
            }

            if (matchedRoot == null)
            {
                continue;
            }

            if (string.Equals(entry.FullName, matchedRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = entry.FullName.Substring(matchedRoot.Length);
            var destination = GetSafePath(serverPath, relativePath);

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                OwnershipHelper.TrySetOwnership(destination, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
                continue;
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                OwnershipHelper.TrySetOwnership(directory, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
            }

            using var entryStream = entry.Open();
            using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(fileStream);
            OwnershipHelper.TrySetOwnership(destination, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
        }
    }

    private static IReadOnlyList<string> GetOverrideModFileNames(ZipArchive archive, params string[] roots)
    {
        var normalizedRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => root.EndsWith("/", StringComparison.Ordinal) ? root : $"{root}/")
            .ToList();
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            string? matchedRoot = null;
            foreach (var root in normalizedRoots)
            {
                if (entry.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    matchedRoot = root;
                    break;
                }
            }

            if (matchedRoot == null)
            {
                continue;
            }

            var relativePath = entry.FullName.Substring(matchedRoot.Length);
            if (!IsModPath(relativePath) || !IsJarFile(relativePath))
            {
                continue;
            }

            results.Add(Path.GetFileName(relativePath));
        }

        return results.ToList();
    }

    private static IReadOnlyList<string> GetArchiveModFileNames(ZipArchive archive, string? rootPrefix)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = TrimArchiveRoot(entry.FullName, rootPrefix);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            if (!IsModPath(relativePath) || !IsJarFile(relativePath))
            {
                continue;
            }

            results.Add(Path.GetFileName(relativePath));
        }

        return results.ToList();
    }

    private static string? GetArchiveRootPrefix(ZipArchive archive)
    {
        string? root = null;
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeArchivePath(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var firstSegment = normalized.Split('/')[0];
            if (string.IsNullOrWhiteSpace(firstSegment))
            {
                continue;
            }

            if (root == null)
            {
                root = firstSegment;
            }
            else if (!string.Equals(root, firstSegment, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return root == null ? null : $"{root}/";
    }

    private static string? TrimArchiveRoot(string fullName, string? rootPrefix)
    {
        var normalized = NormalizeArchivePath(fullName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(rootPrefix) &&
            normalized.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(rootPrefix.Length);
        }

        return normalized;
    }

    private static string? NormalizeArchivePath(string fullName)
    {
        var normalized = fullName.Replace('\\', '/').TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void AddOverrideModRecords(
        List<InstalledModRecord> records,
        IReadOnlyList<string> overrideModFiles,
        string serverName)
    {
        if (overrideModFiles.Count == 0)
        {
            return;
        }

        var existing = new HashSet<string>(
            records.Select(record => record.FileName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in overrideModFiles)
        {
            if (!existing.Add(fileName))
            {
                continue;
            }

            records.Add(new InstalledModRecord
            {
                ServerName = serverName,
                FileName = fileName,
                CurseForgeProjectId = null,
                ModName = Path.GetFileNameWithoutExtension(fileName),
                InstalledAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static bool IsJarFile(string path)
    {
        return path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ModpackDownload(int ProjectId, int FileId, string FileName, string DownloadUrl);

    private async Task<IReadOnlyList<ModpackDownload>> ResolveModpackDownloadsAsync(
        IReadOnlyList<ModpackFile> files,
        CancellationToken cancellationToken,
        Action<string>? log = null)
    {
        if (files.Count == 0)
        {
            return Array.Empty<ModpackDownload>();
        }

        var fileIds = files.Select(f => f.FileId).Distinct().ToList();
        var fileConfigById = files
            .GroupBy(f => f.FileId)
            .ToDictionary(g => g.Key, g => g.First());

        log?.Invoke($"Fetching metadata for {fileIds.Count} files...");
        var fileById = new Dictionary<int, CurseForgeFileDto>();
        try
        {
            var fileDtos = await _curseForgeService.GetModFilesAsync(fileIds, cancellationToken);
            foreach (var file in fileDtos)
            {
                fileById[file.Id] = file;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Bulk metadata fetch failed: {ex.Message}. Falling back to per-file lookups...");
        }

        var missingFiles = fileIds.Where(id => !fileById.ContainsKey(id)).ToList();
        if (missingFiles.Count > 0)
        {
            log?.Invoke($"Missing metadata for {missingFiles.Count} files, requesting individually...");
            foreach (var missingId in missingFiles)
            {
                var fallback = fileConfigById[missingId];
                try
                {
                    var file = await _curseForgeService.GetModFileAsync(
                        fallback.ProjectId,
                        fallback.FileId,
                        cancellationToken);
                    fileById[missingId] = file;
                }
                catch (Exception ex)
                {
                    if (!fallback.Required)
                    {
                        log?.Invoke($"Skipping optional mod file {missingId}: {ex.Message}");
                        continue;
                    }

                    throw;
                }
            }
        }

        var downloadUrls = new Dictionary<int, string>();
        foreach (var file in fileById.Values)
        {
            if (!string.IsNullOrWhiteSpace(file.DownloadUrl))
            {
                downloadUrls[file.Id] = file.DownloadUrl!;
                _logger.LogDebug("File {FileId} ({FileName}) has downloadUrl from metadata: {Url}",
                    file.Id, file.FileName, file.DownloadUrl);
            }
        }

        // Files without downloadUrl in metadata cannot be downloaded (mod author restriction)
        var missingUrls = fileById.Keys.Where(id => !downloadUrls.ContainsKey(id)).ToList();
        if (missingUrls.Count > 0)
        {
            log?.Invoke($"WARNING: {missingUrls.Count} files have no download URL (mod author restriction)");
            _logger.LogWarning("Skipping {Count} files without downloadUrl - mod authors have disabled direct downloads", missingUrls.Count);
            foreach (var fileId in missingUrls)
            {
                var fallback = fileConfigById[fileId];
                var modFile = fileById[fileId];
                log?.Invoke($"SKIPPED: {modFile.FileName} - Direct downloads disabled by mod author");
                log?.Invoke($"  Manual download: https://www.curseforge.com/minecraft/mc-mods/{fallback.ProjectId}/files/{fallback.FileId}");
                _logger.LogWarning("Skipping file {FileId} ({FileName}) for mod {ProjectId} - no downloadUrl available",
                    fallback.FileId, modFile.FileName, fallback.ProjectId);
            }
        }

        var downloads = new List<ModpackDownload>(files.Count);
        foreach (var file in files)
        {
            if (!fileById.TryGetValue(file.FileId, out var modFile))
            {
                if (!file.Required)
                {
                    log?.Invoke($"Skipping optional mod file {file.FileId}: metadata unavailable");
                    continue;
                }

                throw new InvalidOperationException($"Modpack file metadata missing for file ID {file.FileId}");
            }

            if (!downloadUrls.TryGetValue(file.FileId, out var downloadUrl) || string.IsNullOrWhiteSpace(downloadUrl))
            {
                // Skip files without download URLs - mod authors have disabled direct downloads
                log?.Invoke($"SKIPPED: {modFile.FileName} - Mod author requires manual download from CurseForge");
                log?.Invoke($"  Visit: https://www.curseforge.com/minecraft/mc-mods/{file.ProjectId}/files/{file.FileId}");
                continue;
            }

            downloads.Add(new ModpackDownload(
                file.ProjectId,
                file.FileId,
                modFile.FileName,
                downloadUrl));
        }

        return downloads;
    }

    private static void ReportProgress(
        IProgress<JobProgressDto> progress,
        string serverName,
        string type,
        int completed,
        int total,
        string message)
    {
        var percentage = total == 0 ? 0 : (int)Math.Round(completed * 100.0 / total);
        progress.Report(new JobProgressDto(
            string.Empty,
            type,
            serverName,
            "running",
            percentage,
            message,
            DateTimeOffset.UtcNow));
    }

    private async Task DownloadFileAsync(
        string url,
        string targetPath,
        IProgress<JobProgressDto> progress,
        string serverName,
        string type,
        CancellationToken cancellationToken)
    {
        await DownloadFileWithRetriesAsync(
            url,
            targetPath,
            cancellationToken,
            (totalRead, totalBytes) =>
            {
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    var percent = (int)Math.Round(totalRead * 100.0 / totalBytes.Value);
                    progress.Report(new JobProgressDto(
                        string.Empty,
                        type,
                        serverName,
                        "running",
                        percent,
                        $"Downloading {Path.GetFileName(targetPath)}",
                        DateTimeOffset.UtcNow));
                }
            },
            message => progress.Report(new JobProgressDto(
                string.Empty,
                type,
                serverName,
                "running",
                0,
                message,
                DateTimeOffset.UtcNow)));

        await OwnershipHelper.ChangeOwnershipAsync(
            targetPath,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);
    }

    private async Task DownloadFileWithRetriesAsync(
        string url,
        string targetPath,
        CancellationToken cancellationToken,
        Action<long, long?>? progressCallback,
        Action<string>? onRetry)
    {
        // Pre-fetch API key if this is a CurseForge download
        string? curseForgeApiKey = null;
        if (url.Contains("api.curseforge.com", StringComparison.OrdinalIgnoreCase))
        {
            curseForgeApiKey = await _settingsService.GetAsync(SettingsService.Keys.CurseForgeApiKey, cancellationToken);
        }

        for (var attempt = 0; attempt <= DownloadRetryDelays.Length; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Add CurseForge API key for downloads from api.curseforge.com
                if (!string.IsNullOrWhiteSpace(curseForgeApiKey))
                {
                    request.Headers.Add("x-api-key", curseForgeApiKey);
                }

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Download failed for URL: {Url} - Status: {StatusCode} - Body: {Body}",
                        url, (int)response.StatusCode, body);
                    throw new HttpRequestException(
                        $"Download failed ({(int)response.StatusCode}) for URL {url}: {body}",
                        null,
                        response.StatusCode);
                }

                var totalBytes = response.Content.Headers.ContentLength;

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long totalRead = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;
                    progressCallback?.Invoke(totalRead, totalBytes);
                }

                return;
            }
            catch (Exception ex) when (IsRetryableDownloadException(ex) && attempt < DownloadRetryDelays.Length)
            {
                TryDeleteFile(targetPath);
                var delay = DownloadRetryDelays[attempt];
                onRetry?.Invoke($"Download failed ({FormatDownloadError(ex)}). Retrying in {delay.TotalSeconds:0}s...");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsRetryableDownloadException(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        if (ex is HttpRequestException httpEx)
        {
            if (!httpEx.StatusCode.HasValue)
            {
                return true;
            }

            var status = httpEx.StatusCode.Value;
            return status == HttpStatusCode.TooManyRequests
                   || status == HttpStatusCode.Forbidden
                   || (int)status >= 500;
        }

        return ex is IOException;
    }

    private static string FormatDownloadError(Exception ex)
    {
        if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
        {
            return ((int)httpEx.StatusCode.Value).ToString();
        }

        return ex.GetType().Name;
    }

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

    private async Task<int> ResolveFileIdAsync(int modId, int? fileId, CancellationToken cancellationToken)
    {
        if (fileId.HasValue)
        {
            return fileId.Value;
        }

        var mod = await _curseForgeService.GetModAsync(modId, cancellationToken);
        var latestFile = mod.LatestFiles.FirstOrDefault();
        if (latestFile == null)
        {
            throw new InvalidOperationException("No files available for this mod");
        }

        return latestFile.Id;
    }

    private async Task<CurseForgeFileDto?> ResolveServerPackFileAsync(
        int modpackId,
        CurseForgeFileDto modpackFile,
        CancellationToken cancellationToken)
    {
        if (!modpackFile.ServerPackFileId.HasValue || modpackFile.ServerPackFileId.Value <= 0)
        {
            return null;
        }

        try
        {
            return await _curseForgeService.GetModFileAsync(
                modpackId,
                modpackFile.ServerPackFileId.Value,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve CurseForge server pack {ServerPackFileId} for modpack {ModpackId}. Falling back to client pack.",
                modpackFile.ServerPackFileId,
                modpackId);
            return null;
        }
    }

    private async Task<string?> GetServerMinecraftVersionAsync(string serverName, CancellationToken cancellationToken)
    {
        try
        {
            var config = await _serverService.GetServerConfigAsync(serverName, cancellationToken);
            if (string.IsNullOrWhiteSpace(config.Minecraft.Profile))
            {
                return null;
            }

            var profile = await _profileService.GetProfileAsync(config.Minecraft.Profile, cancellationToken);
            return profile?.Version;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Minecraft version for server {ServerName}", serverName);
            return null;
        }
    }

    private static bool IsMinecraftVersionMatch(string serverVersion, IReadOnlyList<string> supportedVersions)
    {
        if (supportedVersions.Count == 0)
        {
            return true;
        }

        if (supportedVersions.Any(version => string.Equals(version, serverVersion, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var majorMinor = GetMajorMinorVersion(serverVersion);
        return supportedVersions.Any(version => string.Equals(version, majorMinor, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetMajorMinorVersion(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    private static string FormatVersionList(IReadOnlyList<string> versions, int limit = 5)
    {
        if (versions.Count <= limit)
        {
            return string.Join(", ", versions);
        }

        var preview = versions.Take(limit);
        return $"{string.Join(", ", preview)} (+{versions.Count - limit} more)";
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetModsPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "mods");

    private string EnsureModsPath(string serverName)
    {
        var path = GetModsPath(serverName);
        Directory.CreateDirectory(path);
        OwnershipHelper.TrySetOwnership(path, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
        return path;
    }

    private string EnsureModpackPath(string serverName)
    {
        var path = Path.Combine(GetServerPath(serverName), "modpacks");
        Directory.CreateDirectory(path);
        OwnershipHelper.TrySetOwnership(path, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
        return path;
    }

    private void MarkRestartRequired(string serverPath)
    {
        try
        {
            var flagPath = Path.Combine(serverPath, RestartFlagFile);
            File.WriteAllText(flagPath, DateTimeOffset.UtcNow.ToString("O"));
            OwnershipHelper.TrySetOwnership(flagPath, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark restart required for {ServerPath}", serverPath);
        }
    }

    private static string ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required");
        }

        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid file name");
        }

        return safeName;
    }

    private static string GetSafePath(string rootPath, string relativePath)
    {
        var combined = Path.Combine(rootPath, relativePath.TrimStart('/', '\\'));
        var normalized = Path.GetFullPath(combined);

        if (!normalized.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid override path");
        }

        return normalized;
    }

    private static bool IsModPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("mods/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ModrinthFileDecision> EvaluateModrinthModFileAsync(
        ModrinthModpackFile file,
        IDictionary<string, ModrinthProjectDto?> projectCache,
        IDictionary<string, ModrinthVersionDto?> versionCache,
        CancellationToken cancellationToken)
    {
        if (!IsModPath(file.Path))
        {
            return new ModrinthFileDecision(file, true, null, null, null, null, null, null);
        }

        var serverEnv = file.Env?.Server;
        var clientEnv = file.Env?.Client;
        if (string.Equals(serverEnv, "unsupported", StringComparison.OrdinalIgnoreCase))
        {
            return new ModrinthFileDecision(file, false, "ENV_CLIENT_ONLY", null, null, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(serverEnv) &&
            string.Equals(clientEnv, "required", StringComparison.OrdinalIgnoreCase))
        {
            return new ModrinthFileDecision(file, false, "ENV_CLIENT_ONLY", null, null, null, null, null);
        }

        ModrinthVersionDto? resolvedVersion = null;
        ModrinthProjectDto? resolvedProject = null;

        if (TryGetModrinthFileHash(file, out var hash, out var algorithm))
        {
            if (!versionCache.TryGetValue(hash, out var version))
            {
                try
                {
                    version = await _modrinthService.GetVersionByFileHashAsync(hash, algorithm, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve Modrinth version metadata for hash {Hash}", hash);
                    version = null;
                }
                versionCache[hash] = version;
            }

            resolvedVersion = version;
            if (version != null && !string.IsNullOrWhiteSpace(version.ProjectId))
            {
                if (!projectCache.TryGetValue(version.ProjectId, out var project))
                {
                    try
                    {
                        project = await _modrinthService.GetProjectAsync(version.ProjectId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve Modrinth project metadata for {ProjectId}", version.ProjectId);
                        project = null;
                    }
                    projectCache[version.ProjectId] = project;
                }

                if (project != null)
                {
                    resolvedProject = project;
                    if (string.Equals(project.ServerSide, "unsupported", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ModrinthFileDecision(
                            file,
                            false,
                            "ENV_CLIENT_ONLY",
                            project.Id,
                            project.Title,
                            version.Name,
                            project,
                            version);
                    }

                    if (string.IsNullOrWhiteSpace(project.ServerSide) ||
                        string.Equals(project.ServerSide, "unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsKnownClientOnlyCategory(project.Categories))
                        {
                            return new ModrinthFileDecision(
                                file,
                                false,
                                "KNOWN_CLIENT_CATEGORY",
                                project.Id,
                                project.Title,
                                version.Name,
                                project,
                                version);
                        }
                    }

                    return new ModrinthFileDecision(
                        file,
                        true,
                        null,
                        project.Id,
                        project.Title,
                        version.Name,
                        project,
                        version);
                }
            }
        }

        return new ModrinthFileDecision(file, true, null, null, null, null, resolvedProject, resolvedVersion);
    }

    private static bool TryGetModrinthFileHash(
        ModrinthModpackFile file,
        out string hash,
        out string algorithm)
    {
        if (file.Hashes.TryGetValue("sha1", out var sha1) && !string.IsNullOrWhiteSpace(sha1))
        {
            hash = sha1;
            algorithm = "sha1";
            return true;
        }

        if (file.Hashes.TryGetValue("sha512", out var sha512) && !string.IsNullOrWhiteSpace(sha512))
        {
            hash = sha512;
            algorithm = "sha512";
            return true;
        }

        hash = string.Empty;
        algorithm = string.Empty;
        return false;
    }

    private static bool IsKnownClientOnlyCategory(IReadOnlyList<string> categories)
    {
        return categories.Any(category => ClientOnlyCategories.Contains(category));
    }

    private async Task<string?> ResolveModrinthLoaderAsync(
        ModrinthModpackIndex manifest,
        string serverName,
        CancellationToken cancellationToken)
    {
        var loaderKey = manifest.Dependencies.Keys
            .FirstOrDefault(key =>
                key.Equals("forge", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("neoforge", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("fabric", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("fabric-loader", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("quilt", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("quilt-loader", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(loaderKey))
        {
            return NormalizeModrinthLoader(loaderKey);
        }

        try
        {
            var config = await _serverService.GetServerConfigAsync(serverName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(config.Minecraft.Profile))
            {
                var profile = await _profileService.GetProfileAsync(config.Minecraft.Profile, cancellationToken);
                if (profile != null)
                {
                    return NormalizeModrinthLoader(profile.Group);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Modrinth loader for server {ServerName}", serverName);
        }

        return null;
    }

    private static string? NormalizeModrinthLoader(string? loader)
    {
        if (string.IsNullOrWhiteSpace(loader))
        {
            return null;
        }

        return loader.Trim().ToLowerInvariant() switch
        {
            "fabric-loader" => "fabric",
            "quilt-loader" => "quilt",
            "forge" => "forge",
            "neoforge" => "neoforge",
            "fabric" => "fabric",
            "quilt" => "quilt",
            _ => null
        };
    }

    private async Task<ModrinthDependencyResolution> ResolveModrinthDependenciesAsync(
        IReadOnlyList<ModrinthFileDecision> installedMods,
        IReadOnlyList<ModrinthFileDecision> excludedMods,
        string? loader,
        string? gameVersion,
        IDictionary<string, ModrinthProjectDto?> projectCache,
        IDictionary<string, ModrinthVersionDto?> versionCache,
        CancellationToken cancellationToken)
    {
        var installs = new List<ModrinthDependencyInstall>();
        var errors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedProjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedVersionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedProjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in installedMods)
        {
            if (!string.IsNullOrWhiteSpace(mod.ProjectId))
            {
                installedProjectIds.Add(mod.ProjectId);
            }

            if (!string.IsNullOrWhiteSpace(mod.Version?.Id))
            {
                installedVersionIds.Add(mod.Version.Id);
            }
        }

        foreach (var excluded in excludedMods)
        {
            if (!string.IsNullOrWhiteSpace(excluded.ProjectId))
            {
                excludedProjectIds.Add(excluded.ProjectId);
            }
        }

        var dependencyQueue = new Queue<ModrinthDependencyDto>();
        var queuedDependencyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in installedMods)
        {
            if (mod.Version == null)
            {
                _logger.LogWarning(
                    "Skipping dependency resolution for {FilePath} because Modrinth metadata is unavailable.",
                    mod.File.Path);
                continue;
            }

            if (mod.Version.Dependencies == null)
            {
                continue;
            }

            foreach (var dependency in mod.Version.Dependencies)
            {
                if (string.Equals(dependency.DependencyType, "required", StringComparison.OrdinalIgnoreCase))
                {
                    var dependencyKey = GetDependencyKey(dependency);
                    if (!string.IsNullOrWhiteSpace(dependencyKey) && queuedDependencyKeys.Add(dependencyKey))
                    {
                        dependencyQueue.Enqueue(dependency);
                    }
                }
            }
        }

        while (dependencyQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dependency = dependencyQueue.Dequeue();

            if (!string.IsNullOrWhiteSpace(dependency.VersionId) &&
                installedVersionIds.Contains(dependency.VersionId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(dependency.ProjectId) &&
                installedProjectIds.Contains(dependency.ProjectId))
            {
                continue;
            }

            var dependencyVersion = await ResolveDependencyVersionAsync(
                dependency,
                loader,
                gameVersion,
                projectCache,
                cancellationToken);

            if (dependencyVersion == null)
            {
                errors.Add(dependency.ProjectId ?? dependency.VersionId ?? "unknown");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(gameVersion) &&
                dependencyVersion.GameVersions.Count > 0 &&
                !dependencyVersion.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{dependencyVersion.ProjectId} (incompatible Minecraft version)");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(loader) &&
                dependencyVersion.Loaders.Count > 0 &&
                !dependencyVersion.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{dependencyVersion.ProjectId} (incompatible loader)");
                continue;
            }

            var dependencyProject = await ResolveDependencyProjectAsync(
                dependencyVersion.ProjectId,
                projectCache,
                cancellationToken);

            if (dependencyProject == null)
            {
                errors.Add(dependency.ProjectId ?? dependency.VersionId ?? "unknown");
                continue;
            }

            if (string.Equals(dependencyProject.ServerSide, "unsupported", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{dependencyProject.Title} (client-only)");
                continue;
            }

            if (string.IsNullOrWhiteSpace(dependencyProject.ServerSide) &&
                IsKnownClientOnlyCategory(dependencyProject.Categories))
            {
                errors.Add($"{dependencyProject.Title} (client-only)");
                continue;
            }

            if (installedProjectIds.Contains(dependencyProject.Id))
            {
                continue;
            }

            if (excludedProjectIds.Contains(dependencyProject.Id))
            {
                errors.Add($"{dependencyProject.Title} (excluded)");
                continue;
            }

            installs.Add(new ModrinthDependencyInstall(
                dependencyProject.Id,
                dependencyProject.Title,
                dependencyVersion));
            installedProjectIds.Add(dependencyProject.Id);
            installedVersionIds.Add(dependencyVersion.Id);

            if (dependencyVersion.Dependencies != null)
            {
                foreach (var child in dependencyVersion.Dependencies)
                {
                    if (string.Equals(child.DependencyType, "required", StringComparison.OrdinalIgnoreCase))
                    {
                        var childKey = GetDependencyKey(child);
                        if (!string.IsNullOrWhiteSpace(childKey) && queuedDependencyKeys.Add(childKey))
                        {
                            dependencyQueue.Enqueue(child);
                        }
                    }
                }
            }
        }

        return new ModrinthDependencyResolution(installs, errors.OrderBy(error => error).ToList());
    }

    private static string? GetDependencyKey(ModrinthDependencyDto dependency)
    {
        if (!string.IsNullOrWhiteSpace(dependency.VersionId))
        {
            return $"version:{dependency.VersionId}";
        }

        if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
        {
            return $"project:{dependency.ProjectId}";
        }

        return null;
    }

    private async Task<ModrinthVersionDto?> ResolveDependencyVersionAsync(
        ModrinthDependencyDto dependency,
        string? loader,
        string? gameVersion,
        IDictionary<string, ModrinthProjectDto?> projectCache,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(dependency.VersionId))
            {
                return await _modrinthService.GetVersionAsync(dependency.VersionId, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
            {
                await ResolveDependencyProjectAsync(dependency.ProjectId, projectCache, cancellationToken);
                var versions = await _modrinthService.GetProjectVersionsAsync(
                    dependency.ProjectId,
                    loader,
                    gameVersion,
                    cancellationToken);
                return versions.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve dependency {Dependency}", dependency.ProjectId ?? dependency.VersionId);
        }

        return null;
    }

    private async Task<ModrinthProjectDto?> ResolveDependencyProjectAsync(
        string projectId,
        IDictionary<string, ModrinthProjectDto?> projectCache,
        CancellationToken cancellationToken)
    {
        if (projectCache.TryGetValue(projectId, out var cached))
        {
            return cached;
        }

        try
        {
            var project = await _modrinthService.GetProjectAsync(projectId, cancellationToken);
            projectCache[projectId] = project;
            return project;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Modrinth project metadata for {ProjectId}", projectId);
            projectCache[projectId] = null;
            return null;
        }
    }

    private static ModrinthVersionFileDto? SelectModrinthFile(ModrinthVersionDto version)
    {
        return version.Files.FirstOrDefault(file => file.Primary)
               ?? version.Files.FirstOrDefault();
    }

    private async Task ExtractZipToModsAsync(string zipPath, string modsPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            // Skip directories and non-JAR files
            if (string.IsNullOrEmpty(entry.Name) || !entry.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Extract only the file name (ignore directory structure)
            var targetPath = Path.Combine(modsPath, entry.Name);

            // Extract the file
            entry.ExtractToFile(targetPath, overwrite: true);

            // Set ownership
            await OwnershipHelper.ChangeOwnershipAsync(
                targetPath,
                _hostOptions.RunAsUid,
                _hostOptions.RunAsGid,
                _logger,
                cancellationToken);

            _logger.LogInformation("Extracted JAR: {FileName}", entry.Name);
        }
    }

    private async Task ExtractTarToModsAsync(string tarPath, string modsPath, CancellationToken cancellationToken)
    {
        // For tar/tar.gz extraction, we'll shell out to tar command (more reliable on Linux)
        var isGzipped = tarPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                        tarPath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

        // Create a temp directory for extraction
        var tempExtractPath = Path.Combine(Path.GetTempPath(), $"mineos_extract_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtractPath);

        try
        {
            // Use tar command to extract
            var tarArgs = isGzipped ? "-xzf" : "-xf";
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"{tarArgs} \"{tarPath}\" -C \"{tempExtractPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                _logger.LogError("Tar extraction failed: {Error}", error);
                throw new InvalidOperationException($"Failed to extract tar archive: {error}");
            }

            // Find all JAR files in the extracted directory
            var jarFiles = Directory.GetFiles(tempExtractPath, "*.jar", SearchOption.AllDirectories);
            foreach (var jarFile in jarFiles)
            {
                var fileName = Path.GetFileName(jarFile);
                var targetPath = Path.Combine(modsPath, fileName);

                // Copy JAR to mods folder
                File.Copy(jarFile, targetPath, overwrite: true);

                // Set ownership
                await OwnershipHelper.ChangeOwnershipAsync(
                    targetPath,
                    _hostOptions.RunAsUid,
                    _hostOptions.RunAsGid,
                    _logger,
                    cancellationToken);

                _logger.LogInformation("Extracted JAR: {FileName}", fileName);
            }
        }
        finally
        {
            // Clean up temp extraction directory
            if (Directory.Exists(tempExtractPath))
            {
                Directory.Delete(tempExtractPath, recursive: true);
            }
        }
    }

    private async Task<string> WriteModrinthInstallReportAsync(
        string serverName,
        string modpackName,
        string? modpackVersion,
        int installedMods,
        IReadOnlyList<ModrinthFileDecision> excludedMods,
        int dependencyCount,
        IReadOnlyList<string> missingDependencies,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        var reportPath = Path.Combine(serverPath, "modrinth-install-report.json");
        var excluded = excludedMods
            .Select(excludedMod => new ModrinthExcludedMod(
                excludedMod.File.Path,
                excludedMod.ProjectId,
                excludedMod.ProjectName ?? Path.GetFileNameWithoutExtension(excludedMod.File.Path),
                excludedMod.VersionName,
                excludedMod.Reason ?? "UNKNOWN"))
            .ToList();

        var report = new ModrinthInstallReport(
            serverName,
            modpackName,
            modpackVersion,
            installedMods,
            excluded.Count,
            dependencyCount,
            missingDependencies,
            DateTimeOffset.UtcNow,
            excluded);

        var json = JsonSerializer.Serialize(report, ReportJsonOptions);
        await File.WriteAllTextAsync(reportPath, json, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(
            reportPath,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);

        return reportPath;
    }

    private sealed class ModpackManifest
    {
        public List<ModpackFile> Files { get; set; } = new();
        public ModpackMinecraft? Minecraft { get; set; }
    }

    private sealed class ModpackMinecraft
    {
        public string? Version { get; set; }
    }

    private sealed class ModpackFile
    {
        public int ProjectId { get; set; }
        public int FileId { get; set; }
        public bool Required { get; set; }
    }

    private sealed class ModrinthModpackIndex
    {
        public int FormatVersion { get; set; }
        public string Name { get; set; } = string.Empty;
        public string VersionId { get; set; } = string.Empty;
        public Dictionary<string, string> Dependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ModrinthModpackFile> Files { get; set; } = new();
    }

    private sealed class ModrinthModpackFile
    {
        public string Path { get; set; } = string.Empty;
        public Dictionary<string, string> Hashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Downloads { get; set; } = new();
        public ModrinthModpackEnv? Env { get; set; }
    }

    private sealed class ModrinthModpackEnv
    {
        public string? Client { get; set; }
        public string? Server { get; set; }
    }

    private sealed record ModrinthFileDecision(
        ModrinthModpackFile File,
        bool ShouldInstall,
        string? Reason,
        string? ProjectId,
        string? ProjectName,
        string? VersionName,
        ModrinthProjectDto? Project,
        ModrinthVersionDto? Version);

    private sealed record ModrinthExcludedMod(
        string Path,
        string? ProjectId,
        string? ProjectName,
        string? Version,
        string Reason);

    private sealed record ModrinthDependencyInstall(
        string ProjectId,
        string ProjectName,
        ModrinthVersionDto Version);

    private sealed record ModrinthDependencyResolution(
        IReadOnlyList<ModrinthDependencyInstall> Installs,
        IReadOnlyList<string> Errors);

    private sealed record ModrinthInstallReport(
        string ServerName,
        string ModpackName,
        string? ModpackVersion,
        int InstalledMods,
        int ExcludedModsCount,
        int DependenciesInstalled,
        IReadOnlyList<string> MissingDependencies,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<ModrinthExcludedMod> ExcludedMods);
}
