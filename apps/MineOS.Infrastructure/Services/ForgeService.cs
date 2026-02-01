using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class ForgeService : IForgeService
{
    private const string PromotionsUrl = "https://files.minecraftforge.net/maven/net/minecraftforge/forge/promotions_slim.json";
    private const string MavenBaseUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static DateTimeOffset? _lastFetch;
    private static List<ForgeVersionDto> _versionCache = new();
    private static readonly ConcurrentDictionary<string, ForgeInstallState> Installations = new();

    private readonly HttpClient _httpClient;
    private readonly HostOptions _hostOptions;
    private readonly ILogger<ForgeService> _logger;
    private readonly IRepository<SystemNotification> _notificationRepo;

    public ForgeService(
        HttpClient httpClient,
        IOptions<HostOptions> hostOptions,
        IRepository<SystemNotification> notificationRepo,
        ILogger<ForgeService> logger)
    {
        _httpClient = httpClient;
        _hostOptions = hostOptions.Value;
        _notificationRepo = notificationRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ForgeVersionDto>> GetVersionsAsync(CancellationToken cancellationToken)
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

    public async Task<IReadOnlyList<ForgeVersionDto>> GetVersionsForMinecraftAsync(
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var allVersions = await GetVersionsAsync(cancellationToken);
        return allVersions
            .Where(v => v.MinecraftVersion == minecraftVersion)
            .OrderByDescending(v => v.IsRecommended)
            .ThenByDescending(v => v.IsLatest)
            .ThenByDescending(v => v.ReleaseDate)
            .ToList();
    }

    public async Task<ForgeInstallResultDto> InstallForgeAsync(
        string minecraftVersion,
        string forgeVersion,
        string serverName,
        CancellationToken cancellationToken)
    {
        var installId = Guid.NewGuid().ToString("N");
        var serverPath = GetServerPath(serverName);

        if (!Directory.Exists(serverPath))
        {
            return new ForgeInstallResultDto(installId, "failed", $"Server '{serverName}' not found");
        }

        var state = new ForgeInstallState(
            installId,
            minecraftVersion,
            forgeVersion,
            serverName,
            serverPath,
            DateTimeOffset.UtcNow);

        Installations[installId] = state;

        // Start installation in background
        _ = Task.Run(async () =>
        {
            try
            {
                await RunInstallationAsync(state, CancellationToken.None);
                await CreateForgeNotificationAsync(state, "completed", null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                state.MarkFailed(ex.Message);
                _logger.LogError(ex, "Forge installation {InstallId} failed", installId);
                await CreateForgeNotificationAsync(state, "failed", ex.Message, CancellationToken.None);
            }
        }, CancellationToken.None);

        return new ForgeInstallResultDto(installId, "started", null);
    }

    public Task<ForgeInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken)
    {
        if (Installations.TryGetValue(installId, out var state))
        {
            return Task.FromResult<ForgeInstallStatusDto?>(state.ToDto());
        }
        return Task.FromResult<ForgeInstallStatusDto?>(null);
    }

    public IReadOnlyList<ForgeInstallStatusDto> GetActiveInstalls()
    {
        return Installations.Values
            .Select(state => state.ToDto())
            .Where(dto => string.Equals(dto.Status, "running", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dto => dto.StartedAt)
            .ToList();
    }

    private async Task<List<ForgeVersionDto>> FetchVersionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(PromotionsUrl, cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("promos", out var promos))
            {
                return new List<ForgeVersionDto>();
            }

            var versions = new Dictionary<string, ForgeVersionDto>();
            var recommendedVersions = new HashSet<string>();
            var latestVersions = new HashSet<string>();

            foreach (var prop in promos.EnumerateObject())
            {
                var key = prop.Name;
                var forgeVersion = prop.Value.GetString();

                if (string.IsNullOrWhiteSpace(forgeVersion))
                    continue;

                // Key format: "1.20.4-recommended" or "1.20.4-latest"
                var parts = key.Split('-');
                if (parts.Length != 2)
                    continue;

                var mcVersion = parts[0];
                var type = parts[1];
                var fullVersion = $"{mcVersion}-{forgeVersion}";

                if (type == "recommended")
                {
                    recommendedVersions.Add(fullVersion);
                }
                else if (type == "latest")
                {
                    latestVersions.Add(fullVersion);
                }

                if (!versions.ContainsKey(fullVersion))
                {
                    versions[fullVersion] = new ForgeVersionDto(
                        mcVersion,
                        forgeVersion,
                        fullVersion,
                        false,
                        false,
                        null);
                }
            }

            // Update recommended/latest flags
            var result = versions.Values
                .Select(v => v with
                {
                    IsRecommended = recommendedVersions.Contains(v.FullVersion),
                    IsLatest = latestVersions.Contains(v.FullVersion)
                })
                .OrderByDescending(v => TryParseVersion(v.MinecraftVersion))
                .ThenByDescending(v => v.IsRecommended)
                .ThenByDescending(v => v.IsLatest)
                .ToList();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Forge versions");
            return new List<ForgeVersionDto>();
        }
    }

    private async Task RunInstallationAsync(ForgeInstallState state, CancellationToken cancellationToken)
    {
        var fullVersion = state.FullVersion;
        var installerUrl = $"{MavenBaseUrl}/{fullVersion}/forge-{fullVersion}-installer.jar";
        var tempDir = Path.Combine(Path.GetTempPath(), $"forge-install-{state.InstallId}");

        try
        {
            Directory.CreateDirectory(tempDir);
            var installerPath = Path.Combine(tempDir, $"forge-{fullVersion}-installer.jar");

            // Step 1: Download installer
            state.UpdateProgress(10, "Downloading Forge installer...");
            _logger.LogInformation("Downloading Forge installer from {Url}", installerUrl);

            using (var response = await _httpClient.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            // Step 2: Run installer
            state.UpdateProgress(40, "Running Forge installer...");
            state.AppendOutput($"Starting Forge installer for {fullVersion}...");
            _logger.LogInformation("Running Forge installer in {ServerPath}", state.ServerPath);

            var psi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-jar \"{installerPath}\" --installServer",
                WorkingDirectory = state.ServerPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Forge installer process");
            }

            // Read output in real-time
            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                    if (line != null)
                    {
                        state.AppendOutput(line);
                        // Update progress based on output patterns
                        if (line.Contains("Downloading"))
                            state.UpdateProgress(50, "Downloading libraries...");
                        else if (line.Contains("Extracting"))
                            state.UpdateProgress(60, "Extracting files...");
                        else if (line.Contains("Processing"))
                            state.UpdateProgress(70, "Processing...");
                    }
                }
            }, cancellationToken);

            var errorTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync(cancellationToken);
                    if (line != null)
                    {
                        state.AppendOutput($"[ERROR] {line}");
                    }
                }
            }, cancellationToken);

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync(cancellationToken);

            state.AppendOutput($"Installer exited with code {process.ExitCode}");

            if (process.ExitCode != 0)
            {
                _logger.LogError("Forge installer failed with exit code {ExitCode}", process.ExitCode);
                throw new InvalidOperationException($"Forge installer failed with exit code {process.ExitCode}");
            }

            // Step 3: Find and configure the run script or jar
            state.UpdateProgress(80, "Configuring server...");
            state.AppendOutput("Looking for server JAR...");

            // Forge creates different files depending on version
            // Modern Forge (1.17+): Creates run.sh/run.bat and libraries folder
            // Legacy Forge: Creates forge-{version}.jar
            var runScript = Path.Combine(state.ServerPath, "run.sh");
            var runBat = Path.Combine(state.ServerPath, "run.bat");

            // Look for forge JARs in the root directory (excluding installer)
            var forgeJars = Directory.GetFiles(state.ServerPath, "*.jar")
                .Where(f => !f.Contains("installer", StringComparison.OrdinalIgnoreCase))
                .ToList();

            string? jarFile = null;

            if (File.Exists(runScript) || File.Exists(runBat))
            {
                // Modern Forge - MUST use args file approach (jar in libraries can't be run with -jar alone)
                _logger.LogInformation("Modern Forge installation detected");
                state.AppendOutput("Modern Forge installation detected");

                // Parse run.sh/run.bat for the args file path - this is REQUIRED for modern Forge
                var scriptToRead = File.Exists(runScript) ? runScript : runBat;
                var runContent = await File.ReadAllTextAsync(scriptToRead, cancellationToken);

                // Look for @libraries/.../unix_args.txt or win_args.txt pattern
                var match = System.Text.RegularExpressions.Regex.Match(runContent, @"@(libraries/[^\s""]+(?:unix|win)_args\.txt)");
                if (match.Success)
                {
                    // Modern Forge uses args files which contain the full classpath
                    jarFile = $"@user_jvm_args.txt @{match.Groups[1].Value}";
                    state.AppendOutput($"Using args file approach: {jarFile}");
                }
                else
                {
                    // Try alternative pattern - some versions might have different format
                    match = System.Text.RegularExpressions.Regex.Match(runContent, @"@(libraries/[^\s""]+args\.txt)");
                    if (match.Success)
                    {
                        jarFile = $"@user_jvm_args.txt @{match.Groups[1].Value}";
                        state.AppendOutput($"Using args file approach: {jarFile}");
                    }
                }
            }

            // Fall back to looking for forge jar in root
            if (jarFile == null)
            {
                var forgeJar = forgeJars.FirstOrDefault(f =>
                    Path.GetFileName(f).StartsWith("forge-", StringComparison.OrdinalIgnoreCase));

                if (forgeJar != null)
                {
                    jarFile = Path.GetFileName(forgeJar);
                    state.AppendOutput($"Found legacy Forge JAR: {jarFile}");
                }
            }

            // Last resort: use any jar that isn't minecraft_server
            if (jarFile == null && forgeJars.Count > 0)
            {
                var anyJar = forgeJars.FirstOrDefault(f =>
                    !Path.GetFileName(f).StartsWith("minecraft_server", StringComparison.OrdinalIgnoreCase));

                if (anyJar != null)
                {
                    jarFile = Path.GetFileName(anyJar);
                    state.AppendOutput($"Using JAR: {jarFile}");
                }
            }

            if (jarFile != null)
            {
                await UpdateServerConfigAsync(state.ServerPath, jarFile, cancellationToken);
                state.AppendOutput($"Server config updated with JAR: {jarFile}");
            }
            else
            {
                state.AppendOutput("Warning: Could not determine server JAR file. You may need to configure it manually.");
                _logger.LogWarning("Could not determine Forge server JAR for {ServerName}", state.ServerName);
            }

            // List files in server directory for debugging
            state.AppendOutput("Files in server directory:");
            foreach (var file in Directory.GetFiles(state.ServerPath).Take(20))
            {
                state.AppendOutput($"  {Path.GetFileName(file)}");
            }

            // Set ownership
            await SetOwnershipRecursiveAsync(state.ServerPath, cancellationToken);

            state.UpdateProgress(100, "Installation complete");
            state.AppendOutput("Forge installation completed successfully!");
            state.MarkCompleted();
            _logger.LogInformation("Forge installation {InstallId} completed successfully", state.InstallId);
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temp directory {TempDir}", tempDir);
            }
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
            path,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken,
            recursive: true);
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private static Version? TryParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        return Version.TryParse(version, out var parsed) ? parsed : null;
    }

    private async Task CreateForgeNotificationAsync(
        ForgeInstallState state,
        string outcome,
        string? error,
        CancellationToken cancellationToken)
    {
        var type = outcome == "completed" ? "success" : "error";
        var title = outcome == "completed" ? "Forge Install Completed" : "Forge Install Failed";
        var version = $"Minecraft {state.MinecraftVersion} / Forge {state.ForgeVersion}";
        var message = outcome == "completed"
            ? $"Forge install for {state.ServerName} ({version}) completed successfully."
            : $"Forge install for {state.ServerName} ({version}) failed: {error ?? "Unknown error"}.";

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

    private sealed class ForgeInstallState
    {
        private readonly object _lock = new();
        private readonly System.Text.StringBuilder _output = new();

        public ForgeInstallState(
            string installId,
            string minecraftVersion,
            string forgeVersion,
            string serverName,
            string serverPath,
            DateTimeOffset startedAt)
        {
            InstallId = installId;
            MinecraftVersion = minecraftVersion;
            ForgeVersion = forgeVersion;
            ServerName = serverName;
            ServerPath = serverPath;
            StartedAt = startedAt;
            Status = "running";
            Progress = 0;
        }

        public string InstallId { get; }
        public string MinecraftVersion { get; }
        public string ForgeVersion { get; }
        public string FullVersion => $"{MinecraftVersion}-{ForgeVersion}";
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
            lock (_lock)
            {
                Progress = progress;
                CurrentStep = step;
            }
        }

        public void AppendOutput(string line)
        {
            lock (_lock)
            {
                _output.AppendLine(line);
                // Keep output under 100KB to prevent memory issues
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
            lock (_lock)
            {
                Status = "completed";
                Progress = 100;
                CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        public void MarkFailed(string error)
        {
            lock (_lock)
            {
                Status = "failed";
                Error = error;
                CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        public ForgeInstallStatusDto ToDto()
        {
            lock (_lock)
            {
                return new ForgeInstallStatusDto(
                    InstallId,
                    MinecraftVersion,
                    ForgeVersion,
                    ServerName,
                    Status,
                    Progress,
                    CurrentStep,
                    Error,
                    _output.ToString(),
                    StartedAt,
                    CompletedAt);
            }
        }
    }
}
