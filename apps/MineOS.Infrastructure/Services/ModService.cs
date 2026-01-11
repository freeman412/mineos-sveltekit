using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class ModService : IModService
{
    private const string RestartFlagFile = ".mineos-restart-required";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<ModService> _logger;
    private readonly HostOptions _hostOptions;
    private readonly ICurseForgeService _curseForgeService;
    private readonly HttpClient _httpClient;

    public ModService(
        ILogger<ModService> logger,
        IOptions<HostOptions> hostOptions,
        ICurseForgeService curseForgeService,
        HttpClient httpClient)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
        _curseForgeService = curseForgeService;
        _httpClient = httpClient;
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
        var targetPath = Path.Combine(modsPath, safeName);

        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(target, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(
            targetPath,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);
        MarkRestartRequired(serverPath);
        _logger.LogInformation("Uploaded mod {FileName} for server {ServerName}", safeName, serverName);
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

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stepMessage = $"Downloading mod {completed}/{total - 1}";
            ReportProgress(progress, serverName, "modpack-install", completed, total, stepMessage);
            var silentProgress = new Progress<JobProgressDto>(_ => { });
            await InstallModFromCurseForgeAsync(serverName, file.ProjectId, file.FileId, silentProgress, cancellationToken);
            completed++;
            ReportProgress(progress, serverName, "modpack-install", completed, total, $"Downloaded mod {file.ProjectId}");
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
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

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
        }

        await OwnershipHelper.ChangeOwnershipAsync(
            targetPath,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);
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
