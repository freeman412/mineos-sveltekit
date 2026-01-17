using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;
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

    private readonly ILogger<ModService> _logger;
    private readonly HostOptions _hostOptions;
    private readonly ICurseForgeService _curseForgeService;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public ModService(
        ILogger<ModService> logger,
        IOptions<HostOptions> hostOptions,
        ICurseForgeService curseForgeService,
        ISettingsService settingsService,
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
        _curseForgeService = curseForgeService;
        _settingsService = settingsService;
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
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
        var downloadUrl = modpackFile.DownloadUrl ??
                          await _curseForgeService.GetModFileDownloadUrlAsync(modpackId, resolvedFileId, cancellationToken);

        var modpackPath = Path.Combine(EnsureModpackPath(serverName), ValidateFileName(modpackFile.FileName));

        progress.Report(new JobProgressDto(string.Empty, "modpack-install", serverName, "running", 0, "Downloading modpack", DateTimeOffset.UtcNow));
        await DownloadFileAsync(downloadUrl, modpackPath, progress, serverName, "modpack-install", cancellationToken);

        await ApplyModpackAsync(serverName, modpackPath, progress, cancellationToken);
        MarkRestartRequired(GetServerPath(serverName));
        _logger.LogInformation("Installed modpack {ModpackId} ({FileName}) for server {ServerName}", modpackId, modpackFile.FileName, serverName);
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
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var installedMods = await db.InstalledModRecords
                .AsNoTracking()
                .Where(r => r.ServerName == serverName)
                .Include(r => r.Modpack)
                .ToListAsync(cancellationToken);

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
        var downloadUrl = modpackFile.DownloadUrl ??
                          await _curseForgeService.GetModFileDownloadUrlAsync(modpackId, resolvedFileId, cancellationToken);

        var modpackPath = Path.Combine(EnsureModpackPath(serverName), ValidateFileName(modpackFile.FileName));

        state.UpdateProgress(5, "Downloading modpack archive");
        state.AppendOutput($"Downloading {modpackFile.FileName}...");
        await DownloadFileWithStateAsync(downloadUrl, modpackPath, state, cancellationToken);

        try
        {
            await ApplyModpackWithStateAsync(serverName, modpackPath, modpackId, modpackName, modpackVersion, logoUrl, state, cancellationToken);
            MarkRestartRequired(serverPath);
            _logger.LogInformation("Installed modpack {ModpackId} ({FileName}) for server {ServerName}", modpackId, modpackFile.FileName, serverName);
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
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var modpacks = await db.InstalledModpacks
                .AsNoTracking()
                .Where(m => m.ServerName == serverName)
                .ToListAsync(cancellationToken);

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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var modpack = await db.InstalledModpacks
            .Include(m => m.Mods)
            .FirstOrDefaultAsync(m => m.Id == modpackDbId && m.ServerName == serverName, cancellationToken);

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

        db.InstalledModpacks.Remove(modpack);
        await db.SaveChangesAsync(cancellationToken);

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
        IModpackInstallState state,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(modpackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

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

        state.SetTotalMods(manifest.Files.Count);
        state.AppendOutput($"Found {manifest.Files.Count} mods to install");

        state.UpdateProgress(10, "Extracting overrides");
        state.AppendOutput("Extracting override files...");
        var extractedOverrides = ExtractOverridesWithTracking(archive, serverName, state);
        state.AppendOutput($"Extracted {extractedOverrides} override files");

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

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check if modpack already exists for this server
        var existingModpack = await db.InstalledModpacks
            .FirstOrDefaultAsync(m => m.ServerName == serverName && m.CurseForgeProjectId == curseForgeProjectId, cancellationToken);

        if (existingModpack != null)
        {
            // Update existing modpack
            existingModpack.Name = modpackName;
            existingModpack.Version = modpackVersion;
            existingModpack.LogoUrl = logoUrl;
            existingModpack.ModCount = installedModRecords.Count;
            existingModpack.InstalledAt = DateTimeOffset.UtcNow;

            // Remove old mod records
            var oldRecords = await db.InstalledModRecords
                .Where(r => r.ModpackId == existingModpack.Id)
                .ToListAsync(cancellationToken);
            db.InstalledModRecords.RemoveRange(oldRecords);

            // Add new mod records
            foreach (var record in installedModRecords)
            {
                record.ModpackId = existingModpack.Id;
            }
            db.InstalledModRecords.AddRange(installedModRecords);
        }
        else
        {
            // Create new modpack
            var modpack = new InstalledModpack
            {
                ServerName = serverName,
                CurseForgeProjectId = curseForgeProjectId,
                Name = modpackName,
                Version = modpackVersion,
                LogoUrl = logoUrl,
                ModCount = installedModRecords.Count,
                InstalledAt = DateTimeOffset.UtcNow
            };
            db.InstalledModpacks.Add(modpack);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var record in installedModRecords)
            {
                record.ModpackId = modpack.Id;
            }
            db.InstalledModRecords.AddRange(installedModRecords);
        }

        await db.SaveChangesAsync(cancellationToken);
        state.AppendOutput($"Installation complete! Installed {installedModRecords.Count} mods.");
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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var modpack = new InstalledModpack
        {
            ServerName = serverName,
            CurseForgeProjectId = curseForgeProjectId,
            Name = modpackName,
            Version = modpackVersion,
            LogoUrl = logoUrl,
            ModCount = modRecords.Count,
            InstalledAt = DateTimeOffset.UtcNow
        };

        db.InstalledModpacks.Add(modpack);
        await db.SaveChangesAsync(cancellationToken);

        // Associate mods with the modpack
        foreach (var record in modRecords)
        {
            record.ModpackId = modpack.Id;
        }

        db.InstalledModRecords.AddRange(modRecords);
        await db.SaveChangesAsync(cancellationToken);
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
        IProgress<JobProgressDto> progress,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(modpackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

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

        var total = manifest.Files.Count + 1;
        var completed = 0;

        progress.Report(new JobProgressDto(string.Empty, "modpack-install", serverName, "running", 0, "Extracting overrides", DateTimeOffset.UtcNow));
        ExtractOverrides(archive, serverName);
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

    private void ExtractOverrides(ZipArchive archive, string serverName)
    {
        var serverPath = GetServerPath(serverName);
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
        }
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

    private sealed class ModpackManifest
    {
        public List<ModpackFile> Files { get; set; } = new();
    }

    private sealed class ModpackFile
    {
        public int ProjectId { get; set; }
        public int FileId { get; set; }
        public bool Required { get; set; }
    }
}
