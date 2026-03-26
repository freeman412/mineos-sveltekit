using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Constants;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class QuiltService : IQuiltService
{
    private const string MetaBaseUrl = "https://meta.quiltmc.org/v3/versions";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly SemaphoreSlim GameVersionCacheLock = new(1, 1);
    private static readonly SemaphoreSlim LoaderVersionCacheLock = new(1, 1);

    private static DateTimeOffset? _gameVersionLastFetch;
    private static DateTimeOffset? _loaderVersionLastFetch;
    private static List<QuiltGameVersionDto> _gameVersionCache = new();
    private static List<QuiltLoaderVersionDto> _loaderVersionCache = new();

    private static readonly ConcurrentDictionary<string, QuiltInstallState> Installations = new();

    private readonly HttpClient _httpClient;
    private readonly HostOptions _hostOptions;
    private readonly ILogger<QuiltService> _logger;
    private readonly IRepository<SystemNotification> _notificationRepo;

    public QuiltService(
        HttpClient httpClient,
        IOptions<HostOptions> hostOptions,
        IRepository<SystemNotification> notificationRepo,
        ILogger<QuiltService> logger)
    {
        _httpClient = httpClient;
        _hostOptions = hostOptions.Value;
        _notificationRepo = notificationRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<QuiltGameVersionDto>> GetGameVersionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_gameVersionLastFetch.HasValue && now - _gameVersionLastFetch.Value < CacheTtl && _gameVersionCache.Count > 0)
            return _gameVersionCache;

        await GameVersionCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_gameVersionLastFetch.HasValue && now - _gameVersionLastFetch.Value < CacheTtl && _gameVersionCache.Count > 0)
                return _gameVersionCache;

            var versions = await FetchGameVersionsAsync(cancellationToken);
            if (versions.Count > 0)
            {
                _gameVersionCache = versions;
                _gameVersionLastFetch = DateTimeOffset.UtcNow;
            }
            return _gameVersionCache;
        }
        finally
        {
            GameVersionCacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<QuiltLoaderVersionDto>> GetLoaderVersionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_loaderVersionLastFetch.HasValue && now - _loaderVersionLastFetch.Value < CacheTtl && _loaderVersionCache.Count > 0)
            return _loaderVersionCache;

        await LoaderVersionCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_loaderVersionLastFetch.HasValue && now - _loaderVersionLastFetch.Value < CacheTtl && _loaderVersionCache.Count > 0)
                return _loaderVersionCache;

            var versions = await FetchLoaderVersionsAsync(cancellationToken);
            if (versions.Count > 0)
            {
                _loaderVersionCache = versions;
                _loaderVersionLastFetch = DateTimeOffset.UtcNow;
            }
            return _loaderVersionCache;
        }
        finally
        {
            LoaderVersionCacheLock.Release();
        }
    }

    public async Task<QuiltInstallResultDto> InstallQuiltAsync(
        string minecraftVersion, string loaderVersion, string serverName,
        CancellationToken cancellationToken)
    {
        var installId = Guid.NewGuid().ToString("N");
        var serverPath = GetServerPath(serverName);

        if (!Directory.Exists(serverPath))
            return new QuiltInstallResultDto(installId, "failed", $"Server '{serverName}' not found");

        var state = new QuiltInstallState(
            installId, minecraftVersion, loaderVersion,
            serverName, serverPath, DateTimeOffset.UtcNow);

        Installations[installId] = state;

        _ = Task.Run(async () =>
        {
            try
            {
                await RunInstallationAsync(state, CancellationToken.None);
                await CreateNotificationAsync(state, "completed", null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                state.MarkFailed(ex.Message);
                _logger.LogError(ex, "Quilt installation {InstallId} failed", installId);
                await CreateNotificationAsync(state, "failed", ex.Message, CancellationToken.None);
            }
        }, CancellationToken.None);

        return new QuiltInstallResultDto(installId, "started", null);
    }

    public Task<QuiltInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken)
    {
        if (Installations.TryGetValue(installId, out var state))
            return Task.FromResult<QuiltInstallStatusDto?>(state.ToDto());
        return Task.FromResult<QuiltInstallStatusDto?>(null);
    }

    public IReadOnlyList<QuiltInstallStatusDto> GetActiveInstalls()
    {
        EvictStaleInstallations();
        return Installations.Values
            .Select(state => state.ToDto())
            .Where(dto => string.Equals(dto.Status, "running", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dto => dto.StartedAt)
            .ToList();
    }

    private static void EvictStaleInstallations()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        foreach (var kvp in Installations)
        {
            if (kvp.Value.CompletedAt.HasValue && kvp.Value.CompletedAt.Value < cutoff)
                Installations.TryRemove(kvp.Key, out _);
        }
    }

    private async Task<List<QuiltGameVersionDto>> FetchGameVersionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync($"{MetaBaseUrl}/game", cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var versions = new List<QuiltGameVersionDto>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var version = element.GetProperty("version").GetString();
                var stable = element.GetProperty("stable").GetBoolean();
                if (!string.IsNullOrWhiteSpace(version))
                    versions.Add(new QuiltGameVersionDto(version, stable));
            }
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Quilt game versions");
            return new List<QuiltGameVersionDto>();
        }
    }

    private async Task<List<QuiltLoaderVersionDto>> FetchLoaderVersionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync($"{MetaBaseUrl}/loader", cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var versions = new List<QuiltLoaderVersionDto>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var version = element.GetProperty("version").GetString();
                // Quilt loader API uses "separator" field instead of "stable" in some versions;
                // check for both patterns
                var stable = true;
                if (element.TryGetProperty("stable", out var stableProp))
                    stable = stableProp.GetBoolean();
                if (!string.IsNullOrWhiteSpace(version))
                    versions.Add(new QuiltLoaderVersionDto(version, stable));
            }
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Quilt loader versions");
            return new List<QuiltLoaderVersionDto>();
        }
    }

    private async Task<string> GetLatestInstallerVersionAsync(CancellationToken cancellationToken)
    {
        var json = await _httpClient.GetStringAsync($"{MetaBaseUrl}/installer", cancellationToken);
        using var doc = JsonDocument.Parse(json);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            // Quilt installer API may use "stable" or just list versions
            if (element.TryGetProperty("stable", out var stable) && stable.GetBoolean())
            {
                var version = element.GetProperty("version").GetString();
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }
        }

        // Fallback to first entry
        var first = doc.RootElement[0].GetProperty("version").GetString();
        return first ?? throw new InvalidOperationException("No Quilt installer versions available");
    }

    private async Task RunInstallationAsync(QuiltInstallState state, CancellationToken cancellationToken)
    {
        var installerVersion = await GetLatestInstallerVersionAsync(cancellationToken);

        var jarUrl = $"{MetaBaseUrl}/loader/{state.MinecraftVersion}/{state.LoaderVersion}/{installerVersion}/server/jar";
        var jarFileName = $"quilt-server-mc.{state.MinecraftVersion}-loader.{state.LoaderVersion}.jar";
        var jarPath = Path.Combine(state.ServerPath, jarFileName);

        state.UpdateProgress(10, "Downloading Quilt server JAR...");
        state.AppendOutput($"Downloading Quilt server for Minecraft {state.MinecraftVersion} with loader {state.LoaderVersion}...");
        _logger.LogInformation("Downloading Quilt server from {Url}", jarUrl);

        using (var response = await _httpClient.GetAsync(jarUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Failed to download Quilt server: {response.StatusCode}. " +
                    $"This Minecraft version ({state.MinecraftVersion}) may not be supported by Quilt.");
            }

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            await using var fs = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None);

            if (totalBytes > 0)
            {
                var buffer = new byte[81920];
                var bytesRead = 0L;
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                int read;
                while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    bytesRead += read;
                    var progress = (int)(10 + (bytesRead * 70 / totalBytes));
                    state.UpdateProgress(progress, $"Downloading... {bytesRead / 1024}KB / {totalBytes / 1024}KB");
                }
            }
            else
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }
        }

        state.AppendOutput($"Downloaded {jarFileName}");

        state.UpdateProgress(85, "Configuring server...");
        state.AppendOutput("Updating server configuration...");

        await UpdateServerConfigAsync(state.ServerPath, jarFileName, cancellationToken);
        state.AppendOutput($"Server config updated with JAR: {jarFileName}");

        // Write server type marker
        var typeFilePath = Path.Combine(state.ServerPath, ".mineos-server-type");
        await File.WriteAllTextAsync(typeFilePath, "quilt", cancellationToken);

        state.UpdateProgress(95, "Setting file ownership...");
        await SetOwnershipRecursiveAsync(state.ServerPath, cancellationToken);

        state.UpdateProgress(100, "Installation complete");
        state.AppendOutput("Quilt installation completed successfully!");
        state.MarkCompleted();
        _logger.LogInformation("Quilt installation {InstallId} completed successfully", state.InstallId);
    }

    private async Task UpdateServerConfigAsync(string serverPath, string jarFile, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(serverPath, "server.config");

        Dictionary<string, Dictionary<string, string>> sections;
        if (File.Exists(configPath))
        {
            var content = await File.ReadAllTextAsync(configPath, cancellationToken);
            sections = IniParser.ParseWithSections(content);
        }
        else
        {
            sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }

        if (!sections.TryGetValue("java", out var javaSection))
        {
            javaSection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            sections["java"] = javaSection;
        }

        javaSection["jarfile"] = jarFile;

        var updated = IniParser.WriteWithSections(sections);
        await File.WriteAllTextAsync(configPath, updated, cancellationToken);
    }

    private async Task SetOwnershipRecursiveAsync(string path, CancellationToken cancellationToken)
    {
        await OwnershipHelper.ChangeOwnershipAsync(
            path, _hostOptions.RunAsUid, _hostOptions.RunAsGid,
            _logger, cancellationToken, recursive: true);
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private async Task CreateNotificationAsync(
        QuiltInstallState state, string outcome, string? error, CancellationToken cancellationToken)
    {
        var type = outcome == JobStatus.Completed ? "success" : "error";
        var title = outcome == JobStatus.Completed ? "Quilt Install Completed" : "Quilt Install Failed";
        var version = $"Minecraft {state.MinecraftVersion} / Loader {state.LoaderVersion}";
        var message = outcome == JobStatus.Completed
            ? $"Quilt install for {state.ServerName} ({version}) completed successfully."
            : $"Quilt install for {state.ServerName} ({version}) failed: {error ?? "Unknown error"}.";

        await _notificationRepo.AddAsync(new SystemNotification
        {
            Type = type,
            Title = title,
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow,
            ServerName = state.ServerName,
            IsRead = false
        }, cancellationToken);
    }

    private sealed class QuiltInstallState
    {
        private readonly object _lock = new();
        private readonly System.Text.StringBuilder _output = new();

        public QuiltInstallState(
            string installId, string minecraftVersion, string loaderVersion,
            string serverName, string serverPath, DateTimeOffset startedAt)
        {
            InstallId = installId;
            MinecraftVersion = minecraftVersion;
            LoaderVersion = loaderVersion;
            ServerName = serverName;
            ServerPath = serverPath;
            StartedAt = startedAt;
            Status = JobStatus.Running;
            Progress = 0;
        }

        public string InstallId { get; }
        public string MinecraftVersion { get; }
        public string LoaderVersion { get; }
        public string ServerName { get; }
        public string ServerPath { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public string Status { get; private set; }
        public int Progress { get; private set; }
        public string? CurrentStep { get; private set; }
        public string? Error { get; private set; }

        public void UpdateProgress(int progress, string step)
        {
            lock (_lock) { Progress = progress; CurrentStep = step; }
        }

        public void AppendOutput(string line)
        {
            lock (_lock)
            {
                _output.AppendLine(line);
                if (_output.Length > 100_000)
                {
                    var str = _output.ToString();
                    _output.Clear();
                    _output.Append(str.Substring(str.Length - 80_000));
                }
            }
        }

        public void MarkCompleted()
        {
            lock (_lock) { Status = JobStatus.Completed; Progress = 100; CompletedAt = DateTimeOffset.UtcNow; }
        }

        public void MarkFailed(string error)
        {
            lock (_lock) { Status = JobStatus.Failed; Error = error; CompletedAt = DateTimeOffset.UtcNow; }
        }

        public QuiltInstallStatusDto ToDto()
        {
            lock (_lock)
            {
                return new QuiltInstallStatusDto(
                    InstallId, MinecraftVersion, LoaderVersion, ServerName,
                    Status, Progress, CurrentStep, Error, _output.ToString(),
                    StartedAt, CompletedAt);
            }
        }
    }
}
