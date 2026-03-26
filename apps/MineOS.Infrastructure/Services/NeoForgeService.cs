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

public sealed class NeoForgeService : INeoForgeService
{
    private const string VersionsUrl = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";
    private const string MavenBaseUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);

    private static DateTimeOffset? _lastFetch;
    private static List<NeoForgeVersionDto> _versionCache = new();

    private static readonly ConcurrentDictionary<string, NeoForgeInstallState> Installations = new();

    private readonly HttpClient _httpClient;
    private readonly HostOptions _hostOptions;
    private readonly ILogger<NeoForgeService> _logger;
    private readonly IRepository<SystemNotification> _notificationRepo;

    public NeoForgeService(
        HttpClient httpClient,
        IOptions<HostOptions> hostOptions,
        IRepository<SystemNotification> notificationRepo,
        ILogger<NeoForgeService> logger)
    {
        _httpClient = httpClient;
        _hostOptions = hostOptions.Value;
        _notificationRepo = notificationRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NeoForgeVersionDto>> GetVersionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastFetch.HasValue && now - _lastFetch.Value < CacheTtl && _versionCache.Count > 0)
        {
            return _versionCache;
        }

        await CacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_lastFetch.HasValue && now - _lastFetch.Value < CacheTtl && _versionCache.Count > 0)
            {
                return _versionCache;
            }

            var versions = await FetchVersionsAsync(cancellationToken);
            if (versions.Count > 0)
            {
                _versionCache = versions;
                _lastFetch = DateTimeOffset.UtcNow;
            }

            return _versionCache;
        }
        finally
        {
            CacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<NeoForgeVersionDto>> GetVersionsForMinecraftAsync(
        string minecraftVersion, CancellationToken cancellationToken)
    {
        var all = await GetVersionsAsync(cancellationToken);
        return all.Where(v => v.MinecraftVersion == minecraftVersion).ToList();
    }

    public async Task<NeoForgeInstallResultDto> InstallNeoForgeAsync(
        string minecraftVersion, string neoForgeVersion, string serverName,
        CancellationToken cancellationToken)
    {
        var installId = Guid.NewGuid().ToString("N");
        var serverPath = GetServerPath(serverName);

        if (!Directory.Exists(serverPath))
        {
            return new NeoForgeInstallResultDto(installId, "failed", $"Server '{serverName}' not found");
        }

        var state = new NeoForgeInstallState(
            installId, minecraftVersion, neoForgeVersion,
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
                _logger.LogError(ex, "NeoForge installation {InstallId} failed", installId);
                await CreateNotificationAsync(state, "failed", ex.Message, CancellationToken.None);
            }
        }, CancellationToken.None);

        return new NeoForgeInstallResultDto(installId, "started", null);
    }

    public Task<NeoForgeInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken)
    {
        if (Installations.TryGetValue(installId, out var state))
        {
            return Task.FromResult<NeoForgeInstallStatusDto?>(state.ToDto());
        }
        return Task.FromResult<NeoForgeInstallStatusDto?>(null);
    }

    public IReadOnlyList<NeoForgeInstallStatusDto> GetActiveInstalls()
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

    /// <summary>
    /// NeoForge versions API returns {"isSnapshot":false,"versions":["20.2.86","20.2.88",...]}.
    /// Version format: {mcMinor}.{mcPatch}.{build} maps to MC 1.{mcMinor}.{mcPatch}.
    /// Starting from MC 1.21: 21.0.x = MC 1.21, 21.1.x = MC 1.21.1, etc.
    /// </summary>
    private async Task<List<NeoForgeVersionDto>> FetchVersionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(VersionsUrl, cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var versions = new List<NeoForgeVersionDto>();
            if (!doc.RootElement.TryGetProperty("versions", out var versionsArray))
            {
                _logger.LogWarning("NeoForge versions API returned unexpected format");
                return versions;
            }

            // Collect all version strings first to determine latest per MC version
            var allVersions = new List<(string neoForgeVersion, string mcVersion)>();

            foreach (var element in versionsArray.EnumerateArray())
            {
                var versionStr = element.GetString();
                if (string.IsNullOrWhiteSpace(versionStr)) continue;

                var mcVersion = NeoForgeVersionToMinecraft(versionStr);
                if (mcVersion != null)
                {
                    allVersions.Add((versionStr, mcVersion));
                }
            }

            // Group by MC version to determine latest
            var grouped = allVersions.GroupBy(v => v.mcVersion);
            foreach (var group in grouped)
            {
                var sorted = group.OrderByDescending(v => v.neoForgeVersion, StringComparer.OrdinalIgnoreCase).ToList();
                for (var i = 0; i < sorted.Count; i++)
                {
                    versions.Add(new NeoForgeVersionDto(
                        sorted[i].mcVersion,
                        sorted[i].neoForgeVersion,
                        IsLatest: i == 0,
                        ReleaseDate: null));
                }
            }

            // Sort: MC version descending, then latest first
            versions.Sort((a, b) =>
            {
                var mcCmp = CompareMinecraftVersions(b.MinecraftVersion, a.MinecraftVersion);
                if (mcCmp != 0) return mcCmp;
                if (a.IsLatest != b.IsLatest) return a.IsLatest ? -1 : 1;
                return string.Compare(b.NeoForgeVersion, a.NeoForgeVersion, StringComparison.OrdinalIgnoreCase);
            });

            _logger.LogInformation("Fetched {Count} NeoForge versions", versions.Count);
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch NeoForge versions");
            return new List<NeoForgeVersionDto>();
        }
    }

    /// <summary>
    /// Convert NeoForge version to Minecraft version.
    /// "20.4.237" → "1.20.4", "21.0.42" → "1.21", "21.1.5" → "1.21.1"
    /// </summary>
    internal static string? NeoForgeVersionToMinecraft(string neoForgeVersion)
    {
        var parts = neoForgeVersion.Split('.');
        if (parts.Length < 3) return null;

        if (!int.TryParse(parts[0], out var mcMinor)) return null;
        if (!int.TryParse(parts[1], out var mcPatch)) return null;

        return mcPatch == 0 ? $"1.{mcMinor}" : $"1.{mcMinor}.{mcPatch}";
    }

    private static int CompareMinecraftVersions(string a, string b)
    {
        var aParts = a.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var bParts = b.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();

        for (var i = 0; i < Math.Max(aParts.Length, bParts.Length); i++)
        {
            var aVal = i < aParts.Length ? aParts[i] : 0;
            var bVal = i < bParts.Length ? bParts[i] : 0;
            if (aVal != bVal) return aVal.CompareTo(bVal);
        }
        return 0;
    }

    private async Task RunInstallationAsync(NeoForgeInstallState state, CancellationToken cancellationToken)
    {
        // Download the NeoForge installer
        var installerUrl = $"{MavenBaseUrl}/{state.NeoForgeVersion}/neoforge-{state.NeoForgeVersion}-installer.jar";
        var tempDir = Path.Combine(Path.GetTempPath(), $"neoforge-install-{state.InstallId}");
        Directory.CreateDirectory(tempDir);
        var installerPath = Path.Combine(tempDir, $"neoforge-{state.NeoForgeVersion}-installer.jar");

        try
        {
            state.UpdateProgress(5, "Downloading NeoForge installer...");
            state.AppendOutput($"Downloading NeoForge {state.NeoForgeVersion} for Minecraft {state.MinecraftVersion}...");
            _logger.LogInformation("Downloading NeoForge installer from {Url}", installerUrl);

            using (var response = await _httpClient.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new InvalidOperationException(
                        $"Failed to download NeoForge installer: {response.StatusCode}. " +
                        $"Version {state.NeoForgeVersion} may not exist.");
                }

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                await using var fs = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);

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
                        var progress = (int)(5 + (bytesRead * 35 / totalBytes));
                        state.UpdateProgress(progress, $"Downloading... {bytesRead / 1024}KB / {totalBytes / 1024}KB");
                    }
                }
                else
                {
                    await response.Content.CopyToAsync(fs, cancellationToken);
                }
            }

            state.AppendOutput("Installer downloaded, running installation...");
            state.UpdateProgress(40, "Running NeoForge installer...");

            // Run the installer (same pattern as Forge)
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-jar \"{installerPath}\" --installServer",
                WorkingDirectory = state.ServerPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();

            var outputTask = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
                {
                    state.AppendOutput(line);
                    if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
                        state.UpdateProgress(50, "Downloading libraries...");
                    else if (line.Contains("Extracting", StringComparison.OrdinalIgnoreCase))
                        state.UpdateProgress(60, "Extracting files...");
                    else if (line.Contains("Processing", StringComparison.OrdinalIgnoreCase))
                        state.UpdateProgress(70, "Processing...");
                }
            }, cancellationToken);

            var errorTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
                {
                    state.AppendOutput($"[ERR] {line}");
                }
            }, cancellationToken);

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"NeoForge installer exited with code {process.ExitCode}");
            }

            state.AppendOutput("Installer completed successfully");
            state.UpdateProgress(80, "Configuring server...");

            // NeoForge modern installs create run.sh/run.bat with @libraries/... args file
            // Detect the args file and configure server.config
            var runScript = Path.Combine(state.ServerPath, "run.sh");
            string jarFile;

            if (File.Exists(runScript))
            {
                var runContent = await File.ReadAllTextAsync(runScript, cancellationToken);
                // run.sh contains: java @libraries/net/neoforged/neoforge/{ver}/unix_args.txt "$@"
                var argsMatch = System.Text.RegularExpressions.Regex.Match(runContent, @"@(libraries[\S]+args\.txt)");
                if (argsMatch.Success)
                {
                    jarFile = $"@{argsMatch.Groups[1].Value}";
                    state.AppendOutput($"Detected NeoForge args file: {jarFile}");
                }
                else
                {
                    // Fallback: look for neoforge jar directly
                    var neoforgeJar = Directory.GetFiles(state.ServerPath, "neoforge-*.jar")
                        .Select(Path.GetFileName)
                        .FirstOrDefault();
                    jarFile = neoforgeJar ?? $"neoforge-{state.NeoForgeVersion}.jar";
                    state.AppendOutput($"Using NeoForge JAR: {jarFile}");
                }
            }
            else
            {
                var neoforgeJar = Directory.GetFiles(state.ServerPath, "neoforge-*.jar")
                    .Select(Path.GetFileName)
                    .FirstOrDefault();
                jarFile = neoforgeJar ?? $"neoforge-{state.NeoForgeVersion}.jar";
                state.AppendOutput($"Using NeoForge JAR: {jarFile}");
            }

            await UpdateServerConfigAsync(state.ServerPath, jarFile, cancellationToken);
            state.AppendOutput($"Server config updated with: {jarFile}");

            // Write server type marker
            var typeFilePath = Path.Combine(state.ServerPath, ".mineos-server-type");
            await File.WriteAllTextAsync(typeFilePath, "neoforge", cancellationToken);

            state.UpdateProgress(95, "Setting file ownership...");
            await SetOwnershipRecursiveAsync(state.ServerPath, cancellationToken);

            state.UpdateProgress(100, "Installation complete");
            state.AppendOutput("NeoForge installation completed successfully!");
            state.MarkCompleted();
            _logger.LogInformation("NeoForge installation {InstallId} completed successfully", state.InstallId);
        }
        finally
        {
            // Clean up temp directory
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up temp directory {TempDir}", tempDir); }
        }
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
        NeoForgeInstallState state, string outcome, string? error, CancellationToken cancellationToken)
    {
        var type = outcome == JobStatus.Completed ? "success" : "error";
        var title = outcome == JobStatus.Completed ? "NeoForge Install Completed" : "NeoForge Install Failed";
        var version = $"Minecraft {state.MinecraftVersion} / NeoForge {state.NeoForgeVersion}";
        var message = outcome == JobStatus.Completed
            ? $"NeoForge install for {state.ServerName} ({version}) completed successfully."
            : $"NeoForge install for {state.ServerName} ({version}) failed: {error ?? "Unknown error"}.";

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

    private sealed class NeoForgeInstallState
    {
        private readonly object _lock = new();
        private readonly System.Text.StringBuilder _output = new();

        public NeoForgeInstallState(
            string installId, string minecraftVersion, string neoForgeVersion,
            string serverName, string serverPath, DateTimeOffset startedAt)
        {
            InstallId = installId;
            MinecraftVersion = minecraftVersion;
            NeoForgeVersion = neoForgeVersion;
            ServerName = serverName;
            ServerPath = serverPath;
            StartedAt = startedAt;
            Status = JobStatus.Running;
            Progress = 0;
        }

        public string InstallId { get; }
        public string MinecraftVersion { get; }
        public string NeoForgeVersion { get; }
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

        public NeoForgeInstallStatusDto ToDto()
        {
            lock (_lock)
            {
                return new NeoForgeInstallStatusDto(
                    InstallId, MinecraftVersion, NeoForgeVersion, ServerName,
                    Status, Progress, CurrentStep, Error, _output.ToString(),
                    StartedAt, CompletedAt);
            }
        }
    }
}
