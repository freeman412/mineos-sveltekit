# Server Creation Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the server creation wizard into a two-tier category/implementation flow, add NeoForge and Quilt modloader support, remove CurseForge as a creation path, and decompose the monolithic wizard into reusable components.

**Architecture:** Backend adds NeoForgeService and QuiltService following the exact patterns of ForgeService and FabricService respectively. Frontend replaces the 1800-line monolithic wizard with a step-based component architecture using shared SelectionCard, InstallProgress, and TwoColumnVersionPicker components. The wizard state flows through a shell component that manages step transitions.

**Tech Stack:** C# / ASP.NET Core (backend services + minimal API endpoints), SvelteKit / Svelte 5 runes (frontend), SSE for install progress streaming.

**Spec:** `docs/superpowers/specs/2026-03-26-server-creation-revamp-design.md`

---

## File Structure

### Backend — New Files
- `apps/MineOS.Application/Interfaces/INeoForgeService.cs` — Interface + DTOs for NeoForge
- `apps/MineOS.Application/Dtos/NeoForgeDtos.cs` — NeoForge DTOs (separate file like FabricDtos.cs)
- `apps/MineOS.Infrastructure/Services/NeoForgeService.cs` — NeoForge version fetching + installation
- `apps/MineOS.Api/Endpoints/NeoForgeEndpoints.cs` — NeoForge API endpoints
- `apps/MineOS.Application/Interfaces/IQuiltService.cs` — Interface for Quilt
- `apps/MineOS.Application/Dtos/QuiltDtos.cs` — Quilt DTOs
- `apps/MineOS.Infrastructure/Services/QuiltService.cs` — Quilt version fetching + installation
- `apps/MineOS.Api/Endpoints/QuiltEndpoints.cs` — Quilt API endpoints

### Backend — Modified Files
- `apps/MineOS.Api/Program.cs:165-166` — Register NeoForge + Quilt services
- `apps/MineOS.Api/Endpoints/ApiEndpoints.cs:19-20` — Map NeoForge + Quilt endpoints
- `apps/MineOS.Infrastructure/Services/ServerService.cs:58-72` — Recognize neoforge/quilt in DetectServerType

### Frontend — New Files
- `apps/web/src/lib/components/SelectionCard.svelte` — Reusable selection card with icon, title, description, badge
- `apps/web/src/lib/components/InstallProgress.svelte` — SSE-based install progress with expandable output
- `apps/web/src/lib/components/TwoColumnVersionPicker.svelte` — MC version left, loader version right
- `apps/web/src/routes/(app)/servers/new/steps/CategorySelect.svelte` — Step 1: category selection
- `apps/web/src/routes/(app)/servers/new/steps/ImplementationSelect.svelte` — Step 2: implementation selection
- `apps/web/src/routes/(app)/servers/new/steps/VersionSelect.svelte` — Step 3: delegates to correct picker
- `apps/web/src/routes/(app)/servers/new/steps/ServerName.svelte` — Step 4: name input
- `apps/web/src/routes/(app)/servers/new/steps/Creating.svelte` — Step 5: installation progress
- `apps/web/src/routes/(app)/servers/new/version-pickers/VanillaVersions.svelte` — Vanilla/Paper/Spigot/CraftBukkit versions
- `apps/web/src/routes/(app)/servers/new/version-pickers/ForgeVersions.svelte` — Forge version picker
- `apps/web/src/routes/(app)/servers/new/version-pickers/NeoForgeVersions.svelte` — NeoForge version picker
- `apps/web/src/routes/(app)/servers/new/version-pickers/FabricVersions.svelte` — Fabric version picker
- `apps/web/src/routes/(app)/servers/new/version-pickers/QuiltVersions.svelte` — Quilt version picker
- `apps/web/src/routes/(app)/servers/new/version-pickers/BedrockVersions.svelte` — Bedrock version picker
- `apps/web/src/routes/(app)/servers/new/version-pickers/TemplateSelect.svelte` — Clone source picker

### Frontend — Modified Files
- `apps/web/src/lib/api/types.ts:363-422` — Add NeoForge + Quilt types
- `apps/web/src/lib/api/client.ts:387-501` — Add NeoForge + Quilt API functions
- `apps/web/src/routes/(app)/servers/new/+page.svelte` — Complete rewrite to wizard shell
- `apps/web/src/routes/(app)/servers/new/+page.server.ts` — No changes needed (already loads profiles + servers)

---

## Phase 1: Backend — NeoForge Support

### Task 1: NeoForge Interface & DTOs

**Files:**
- Create: `apps/MineOS.Application/Interfaces/INeoForgeService.cs`
- Create: `apps/MineOS.Application/Dtos/NeoForgeDtos.cs`

- [ ] **Step 1: Create NeoForge DTOs**

```csharp
// apps/MineOS.Application/Dtos/NeoForgeDtos.cs
namespace MineOS.Application.Dtos;

public record NeoForgeVersionDto(
    string MinecraftVersion,
    string NeoForgeVersion,
    bool IsLatest,
    DateTimeOffset? ReleaseDate);

public record NeoForgeInstallResultDto(
    string InstallId,
    string Status,
    string? Error);

public record NeoForgeInstallStatusDto(
    string InstallId,
    string MinecraftVersion,
    string NeoForgeVersion,
    string ServerName,
    string Status,
    int Progress,
    string? CurrentStep,
    string? Error,
    string? Output,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
```

- [ ] **Step 2: Create NeoForge service interface**

```csharp
// apps/MineOS.Application/Interfaces/INeoForgeService.cs
using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface INeoForgeService
{
    Task<IReadOnlyList<NeoForgeVersionDto>> GetVersionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NeoForgeVersionDto>> GetVersionsForMinecraftAsync(string minecraftVersion, CancellationToken cancellationToken);
    Task<NeoForgeInstallResultDto> InstallNeoForgeAsync(string minecraftVersion, string neoForgeVersion, string serverName, CancellationToken cancellationToken);
    Task<NeoForgeInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken);
    IReadOnlyList<NeoForgeInstallStatusDto> GetActiveInstalls();
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build apps/MineOS.Application/MineOS.Application.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add apps/MineOS.Application/Dtos/NeoForgeDtos.cs apps/MineOS.Application/Interfaces/INeoForgeService.cs
git commit -m "feat: add NeoForge DTOs and service interface"
```

---

### Task 2: NeoForge Service Implementation

**Files:**
- Create: `apps/MineOS.Infrastructure/Services/NeoForgeService.cs`

NeoForge versions come from `https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge` which returns `{"isSnapshot":false,"versions":["20.2.86","20.2.88",...]}`. Version numbers encode the MC version: `20.4.x` = MC 1.20.4, `21.0.x` = MC 1.21, `21.1.x` = MC 1.21.1, etc. The installer JAR is at `https://maven.neoforged.net/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar`.

- [ ] **Step 1: Create NeoForgeService**

```csharp
// apps/MineOS.Infrastructure/Services/NeoForgeService.cs
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
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/NeoForgeService.cs
git commit -m "feat: implement NeoForgeService with version fetching and installer support"
```

---

### Task 3: NeoForge Endpoints & DI Registration

**Files:**
- Create: `apps/MineOS.Api/Endpoints/NeoForgeEndpoints.cs`
- Modify: `apps/MineOS.Api/Program.cs:165-166`
- Modify: `apps/MineOS.Api/Endpoints/ApiEndpoints.cs:19-20`

- [ ] **Step 1: Create NeoForge endpoints**

```csharp
// apps/MineOS.Api/Endpoints/NeoForgeEndpoints.cs
using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class NeoForgeEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapNeoForgeEndpoints(this IEndpointRouteBuilder api)
    {
        var neoforge = api.MapGroup("/neoforge")
            .WithTags("NeoForge")
            .RequireAuthorization();

        neoforge.MapGet("/versions", async (
            INeoForgeService neoForgeService,
            CancellationToken cancellationToken) =>
        {
            var versions = await neoForgeService.GetVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetNeoForgeVersions")
          .WithSummary("Get all NeoForge versions");

        neoforge.MapGet("/versions/{minecraftVersion}", async (
            string minecraftVersion,
            INeoForgeService neoForgeService,
            CancellationToken cancellationToken) =>
        {
            var versions = await neoForgeService.GetVersionsForMinecraftAsync(minecraftVersion, cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetNeoForgeVersionsForMinecraft")
          .WithSummary("Get NeoForge versions for a specific Minecraft version");

        neoforge.MapPost("/install", async (
            [FromBody] NeoForgeInstallRequest request,
            INeoForgeService neoForgeService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
                return Results.BadRequest(new { error = "Minecraft version is required" });
            if (string.IsNullOrWhiteSpace(request.NeoForgeVersion))
                return Results.BadRequest(new { error = "NeoForge version is required" });
            if (string.IsNullOrWhiteSpace(request.ServerName))
                return Results.BadRequest(new { error = "Server name is required" });

            var result = await neoForgeService.InstallNeoForgeAsync(
                request.MinecraftVersion, request.NeoForgeVersion,
                request.ServerName, cancellationToken);

            if (result.Status == "failed")
                return Results.BadRequest(new { error = result.Error });

            return Results.Accepted($"/api/v1/neoforge/install/{result.InstallId}", new { data = result });
        }).WithName("InstallNeoForge")
          .WithSummary("Start NeoForge installation for a server");

        neoforge.MapGet("/install/{installId}", async (
            string installId,
            INeoForgeService neoForgeService,
            CancellationToken cancellationToken) =>
        {
            var status = await neoForgeService.GetInstallStatusAsync(installId, cancellationToken);
            if (status == null)
                return Results.NotFound(new { error = $"Installation '{installId}' not found" });
            return Results.Ok(new { data = status });
        }).WithName("GetNeoForgeInstallStatus")
          .WithSummary("Get NeoForge installation status");

        neoforge.MapGet("/install/{installId}/stream", async (
            HttpContext context,
            string installId,
            INeoForgeService neoForgeService) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var ct = context.RequestAborted;
            while (!ct.IsCancellationRequested)
            {
                var status = await neoForgeService.GetInstallStatusAsync(installId, ct);
                if (status == null)
                {
                    await context.Response.WriteAsync($"data: {{\"status\":\"completed\",\"progress\":100}}\n\n", ct);
                    break;
                }

                var json = System.Text.Json.JsonSerializer.Serialize(status, CamelCaseJsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);

                if (status.Status is "completed" or "failed")
                    break;

                await Task.Delay(1000, ct);
            }
        }).WithName("StreamNeoForgeInstallStatus")
          .WithSummary("Stream NeoForge installation progress via SSE");

        return api;
    }
}

public record NeoForgeInstallRequest(string MinecraftVersion, string NeoForgeVersion, string ServerName);
```

- [ ] **Step 2: Register NeoForge service in DI**

In `apps/MineOS.Api/Program.cs`, add after line 166 (`AddHttpClient<IFabricService, FabricService>()`):

```csharp
builder.Services.AddHttpClient<INeoForgeService, NeoForgeService>();
```

- [ ] **Step 3: Map NeoForge endpoints**

In `apps/MineOS.Api/Endpoints/ApiEndpoints.cs`, add after line 20 (`api.MapFabricEndpoints()`):

```csharp
api.MapNeoForgeEndpoints();
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build apps/MineOS.Api/MineOS.Api.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add apps/MineOS.Api/Endpoints/NeoForgeEndpoints.cs apps/MineOS.Api/Program.cs apps/MineOS.Api/Endpoints/ApiEndpoints.cs
git commit -m "feat: add NeoForge API endpoints and wire up DI"
```

---

## Phase 2: Backend — Quilt Support

### Task 4: Quilt Interface & DTOs

**Files:**
- Create: `apps/MineOS.Application/Interfaces/IQuiltService.cs`
- Create: `apps/MineOS.Application/Dtos/QuiltDtos.cs`

- [ ] **Step 1: Create Quilt DTOs**

```csharp
// apps/MineOS.Application/Dtos/QuiltDtos.cs
namespace MineOS.Application.Dtos;

public record QuiltGameVersionDto(
    string Version,
    bool IsStable);

public record QuiltLoaderVersionDto(
    string Version,
    bool IsStable);

public record QuiltInstallResultDto(
    string InstallId,
    string Status,
    string? Error);

public record QuiltInstallStatusDto(
    string InstallId,
    string MinecraftVersion,
    string LoaderVersion,
    string ServerName,
    string Status,
    int Progress,
    string? CurrentStep,
    string? Error,
    string? Output,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
```

- [ ] **Step 2: Create Quilt service interface**

```csharp
// apps/MineOS.Application/Interfaces/IQuiltService.cs
using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IQuiltService
{
    Task<IReadOnlyList<QuiltGameVersionDto>> GetGameVersionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<QuiltLoaderVersionDto>> GetLoaderVersionsAsync(CancellationToken cancellationToken);
    Task<QuiltInstallResultDto> InstallQuiltAsync(
        string minecraftVersion, string loaderVersion, string serverName,
        CancellationToken cancellationToken);
    Task<QuiltInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken);
    IReadOnlyList<QuiltInstallStatusDto> GetActiveInstalls();
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build apps/MineOS.Application/MineOS.Application.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add apps/MineOS.Application/Dtos/QuiltDtos.cs apps/MineOS.Application/Interfaces/IQuiltService.cs
git commit -m "feat: add Quilt DTOs and service interface"
```

---

### Task 5: Quilt Service Implementation

**Files:**
- Create: `apps/MineOS.Infrastructure/Services/QuiltService.cs`

Quilt's meta API at `https://meta.quiltmc.org/v3/versions` follows the same structure as Fabric's meta API. Game versions at `/game`, loader versions at `/loader`, installer versions at `/installer`. Server JAR URL: `https://meta.quiltmc.org/v3/versions/loader/{gameVersion}/{loaderVersion}/{installerVersion}/server/jar`.

- [ ] **Step 1: Create QuiltService**

```csharp
// apps/MineOS.Infrastructure/Services/QuiltService.cs
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
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/QuiltService.cs
git commit -m "feat: implement QuiltService with version fetching and JAR download"
```

---

### Task 6: Quilt Endpoints & DI Registration

**Files:**
- Create: `apps/MineOS.Api/Endpoints/QuiltEndpoints.cs`
- Modify: `apps/MineOS.Api/Program.cs` (add after NeoForge registration from Task 3)
- Modify: `apps/MineOS.Api/Endpoints/ApiEndpoints.cs` (add after NeoForge mapping from Task 3)

- [ ] **Step 1: Create Quilt endpoints**

```csharp
// apps/MineOS.Api/Endpoints/QuiltEndpoints.cs
using Microsoft.AspNetCore.Mvc;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class QuiltEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapQuiltEndpoints(this IEndpointRouteBuilder api)
    {
        var quilt = api.MapGroup("/quilt")
            .WithTags("Quilt")
            .RequireAuthorization();

        quilt.MapGet("/game-versions", async (
            IQuiltService quiltService,
            CancellationToken cancellationToken) =>
        {
            var versions = await quiltService.GetGameVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetQuiltGameVersions")
          .WithSummary("Get Minecraft versions supported by Quilt");

        quilt.MapGet("/loader-versions", async (
            IQuiltService quiltService,
            CancellationToken cancellationToken) =>
        {
            var versions = await quiltService.GetLoaderVersionsAsync(cancellationToken);
            return Results.Ok(new { data = versions });
        }).WithName("GetQuiltLoaderVersions")
          .WithSummary("Get available Quilt loader versions");

        quilt.MapPost("/install", async (
            [FromBody] QuiltInstallRequest request,
            IQuiltService quiltService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
                return Results.BadRequest(new { error = "Minecraft version is required" });
            if (string.IsNullOrWhiteSpace(request.LoaderVersion))
                return Results.BadRequest(new { error = "Loader version is required" });
            if (string.IsNullOrWhiteSpace(request.ServerName))
                return Results.BadRequest(new { error = "Server name is required" });

            var result = await quiltService.InstallQuiltAsync(
                request.MinecraftVersion, request.LoaderVersion,
                request.ServerName, cancellationToken);

            if (result.Status == "failed")
                return Results.BadRequest(new { error = result.Error });

            return Results.Accepted($"/api/v1/quilt/install/{result.InstallId}", new { data = result });
        }).WithName("InstallQuilt")
          .WithSummary("Start Quilt installation for a server");

        quilt.MapGet("/install/{installId}", async (
            string installId,
            IQuiltService quiltService,
            CancellationToken cancellationToken) =>
        {
            var status = await quiltService.GetInstallStatusAsync(installId, cancellationToken);
            if (status == null)
                return Results.NotFound(new { error = $"Installation '{installId}' not found" });
            return Results.Ok(new { data = status });
        }).WithName("GetQuiltInstallStatus")
          .WithSummary("Get Quilt installation status");

        quilt.MapGet("/install/{installId}/stream", async (
            HttpContext context,
            string installId,
            IQuiltService quiltService) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var ct = context.RequestAborted;
            while (!ct.IsCancellationRequested)
            {
                var status = await quiltService.GetInstallStatusAsync(installId, ct);
                if (status == null)
                {
                    await context.Response.WriteAsync($"data: {{\"status\":\"completed\",\"progress\":100}}\n\n", ct);
                    break;
                }

                var json = System.Text.Json.JsonSerializer.Serialize(status, CamelCaseJsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);

                if (status.Status is "completed" or "failed")
                    break;

                await Task.Delay(1000, ct);
            }
        }).WithName("StreamQuiltInstallStatus")
          .WithSummary("Stream Quilt installation progress via SSE");

        return api;
    }
}

public record QuiltInstallRequest(string MinecraftVersion, string LoaderVersion, string ServerName);
```

- [ ] **Step 2: Register Quilt service in DI**

In `apps/MineOS.Api/Program.cs`, add after the NeoForge registration line:

```csharp
builder.Services.AddHttpClient<IQuiltService, QuiltService>();
```

- [ ] **Step 3: Map Quilt endpoints**

In `apps/MineOS.Api/Endpoints/ApiEndpoints.cs`, add after the NeoForge mapping line:

```csharp
api.MapQuiltEndpoints();
```

- [ ] **Step 4: Verify full backend compiles**

Run: `dotnet build apps/MineOS.Api/MineOS.Api.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add apps/MineOS.Api/Endpoints/QuiltEndpoints.cs apps/MineOS.Api/Program.cs apps/MineOS.Api/Endpoints/ApiEndpoints.cs
git commit -m "feat: add Quilt API endpoints and wire up DI"
```

---

### Task 7: Update ServerService for NeoForge/Quilt Detection

**Files:**
- Modify: `apps/MineOS.Infrastructure/Services/ServerService.cs:58-72`

The `DetectServerType` method reads `.mineos-server-type` and returns whatever string is in it. Since NeoForge and Quilt already write "neoforge"/"quilt" to this file during installation, detection already works. However, we should verify the TPS monitoring config recognizes these types.

- [ ] **Step 1: Check TPS monitoring config for neoforge/quilt support**

Search for where TPS commands are mapped to server types. Run:
```bash
grep -rn "neoforge\|quilt\|tps" apps/MineOS.Infrastructure/Services/ServerService.cs | head -20
```

If neoforge TPS is already mapped (it should be based on exploration), verify quilt is handled (should default to disabled like Fabric). If quilt needs explicit handling, add it.

- [ ] **Step 2: Verify detection works end-to-end**

The `.mineos-server-type` file is written by NeoForgeService (Task 2) and QuiltService (Task 5) during installation. `DetectServerType` reads this file generically — no code change needed there. Confirm by reading the method.

- [ ] **Step 3: Commit if changes were needed**

```bash
git add apps/MineOS.Infrastructure/Services/ServerService.cs
git commit -m "fix: ensure TPS monitoring handles neoforge and quilt server types"
```

---

## Phase 3: Frontend — API Layer

### Task 8: Add NeoForge & Quilt Types and API Client Functions

**Files:**
- Modify: `apps/web/src/lib/api/types.ts` (after line 422)
- Modify: `apps/web/src/lib/api/client.ts` (after Fabric functions ~line 501)

- [ ] **Step 1: Add NeoForge types to types.ts**

After the Fabric types section (line 422), add:

```typescript
// NeoForge types
export type NeoForgeVersion = {
	minecraftVersion: string;
	neoForgeVersion: string;
	isLatest: boolean;
	releaseDate: string | null;
};

export type NeoForgeInstallResult = {
	installId: string;
	status: string;
	error: string | null;
};

export type NeoForgeInstallStatus = {
	installId: string;
	minecraftVersion: string;
	neoForgeVersion: string;
	serverName: string;
	status: string;
	progress: number;
	currentStep: string | null;
	error: string | null;
	output: string | null;
	startedAt: string;
	completedAt: string | null;
};

// Quilt types
export type QuiltGameVersion = {
	version: string;
	isStable: boolean;
};

export type QuiltLoaderVersion = {
	version: string;
	isStable: boolean;
};

export type QuiltInstallResult = {
	installId: string;
	status: string;
	error: string | null;
};

export type QuiltInstallStatus = {
	installId: string;
	minecraftVersion: string;
	loaderVersion: string;
	serverName: string;
	status: string;
	progress: number;
	currentStep: string | null;
	error: string | null;
	output: string | null;
	startedAt: string;
	completedAt: string | null;
};
```

- [ ] **Step 2: Add NeoForge API functions to client.ts**

After the Fabric API functions section, add:

```typescript
// ── NeoForge ──

export async function getNeoForgeVersions(
	fetcher: Fetcher
): Promise<ApiResult<import('./types').NeoForgeVersion[]>> {
	const result = await apiFetch<{ data: import('./types').NeoForgeVersion[] }>(
		fetcher,
		'/api/neoforge/versions'
	);
	if (result.error) return { data: null, error: result.error };
	return { data: result.data?.data ?? null, error: null };
}

export async function getNeoForgeVersionsForMinecraft(
	fetcher: Fetcher,
	minecraftVersion: string
): Promise<ApiResult<import('./types').NeoForgeVersion[]>> {
	const result = await apiFetch<{ data: import('./types').NeoForgeVersion[] }>(
		fetcher,
		`/api/neoforge/versions/${encodeURIComponent(minecraftVersion)}`
	);
	if (result.error) return { data: null, error: result.error };
	return { data: result.data?.data ?? null, error: null };
}

export async function installNeoForge(
	fetcher: Fetcher,
	minecraftVersion: string,
	neoForgeVersion: string,
	serverName: string
): Promise<ApiResult<import('./types').NeoForgeInstallResult>> {
	const result = await apiFetch<{ data: import('./types').NeoForgeInstallResult }>(
		fetcher,
		'/api/neoforge/install',
		{
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ minecraftVersion, neoForgeVersion, serverName })
		}
	);
	if (result.error) return { data: null, error: result.error };
	return { data: result.data?.data ?? null, error: null };
}

// ── Quilt ──

export async function getQuiltGameVersions(
	fetcher: Fetcher
): Promise<ApiResult<import('./types').QuiltGameVersion[]>> {
	const result = await apiFetch<{ data: import('./types').QuiltGameVersion[] }>(
		fetcher,
		'/api/quilt/game-versions'
	);
	if (result.error) return { data: null, error: result.error };
	return { data: result.data?.data ?? null, error: null };
}

export async function getQuiltLoaderVersions(
	fetcher: Fetcher
): Promise<ApiResult<import('./types').QuiltLoaderVersion[]>> {
	const result = await apiFetch<{ data: import('./types').QuiltLoaderVersion[] }>(
		fetcher,
		'/api/quilt/loader-versions'
	);
	if (result.error) return { data: null, error: result.error };
	return { data: result.data?.data ?? null, error: null };
}

export async function installQuilt(
	fetcher: Fetcher,
	minecraftVersion: string,
	loaderVersion: string,
	serverName: string
): Promise<ApiResult<import('./types').QuiltInstallResult>> {
	const result = await apiFetch<{ data: import('./types').QuiltInstallResult }>(
		fetcher,
		'/api/quilt/install',
		{
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ minecraftVersion, loaderVersion, serverName })
		}
	);
	if (result.error) return { data: null, error: result.error };
	return { data: result.data?.data ?? null, error: null };
}

// ── Profile management (used by wizard for profile-based server types) ──

export async function downloadProfile(
	fetcher: Fetcher,
	profileId: string
): Promise<ApiResult<void>> {
	return apiMutate(fetcher, `/api/host/profiles/${encodeURIComponent(profileId)}/download`, 'POST');
}

export async function copyProfileToServer(
	fetcher: Fetcher,
	profileId: string,
	serverName: string
): Promise<ApiResult<void>> {
	return apiMutate(fetcher, `/api/host/profiles/${encodeURIComponent(profileId)}/copy-to-server`, 'POST', { serverName });
}
```

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/lib/api/types.ts apps/web/src/lib/api/client.ts
git commit -m "feat: add NeoForge and Quilt types and API client functions"
```

---

## Phase 4: Frontend — Shared Components

### Task 9: SelectionCard Component

**Files:**
- Create: `apps/web/src/lib/components/SelectionCard.svelte`

- [ ] **Step 1: Create SelectionCard component**

```svelte
<!-- apps/web/src/lib/components/SelectionCard.svelte -->
<script lang="ts">
	interface Props {
		title: string;
		description: string;
		icon?: string;
		iconImage?: string;
		color?: string;
		badge?: string;
		selected?: boolean;
		onclick?: () => void;
	}

	let { title, description, icon, iconImage, color = '#6b7280', badge, selected = false, onclick }: Props = $props();
</script>

<button
	class="selection-card"
	class:selected
	style="--card-color: {color}"
	onclick={onclick}
	type="button"
>
	<div class="card-icon">
		{#if iconImage}
			<img src={iconImage} alt={title} class="icon-image" />
		{:else if icon}
			<span class="icon-emoji">{icon}</span>
		{/if}
	</div>
	<div class="card-content">
		<div class="card-header">
			<h3>{title}</h3>
			{#if badge}
				<span class="badge">{badge}</span>
			{/if}
		</div>
		<p>{description}</p>
	</div>
</button>

<style>
	.selection-card {
		display: flex;
		align-items: center;
		gap: 1rem;
		padding: 1rem 1.25rem;
		border: 2px solid var(--border-color, #374151);
		border-radius: 0.75rem;
		background: var(--card-bg, #1f2937);
		cursor: pointer;
		transition: all 0.15s ease;
		text-align: left;
		width: 100%;
		color: inherit;
		font-family: inherit;
	}

	.selection-card:hover {
		border-color: var(--card-color);
		background: color-mix(in srgb, var(--card-color) 8%, var(--card-bg, #1f2937));
	}

	.selection-card.selected {
		border-color: var(--card-color);
		background: color-mix(in srgb, var(--card-color) 15%, var(--card-bg, #1f2937));
		box-shadow: 0 0 0 1px var(--card-color);
	}

	.card-icon {
		flex-shrink: 0;
		width: 2.5rem;
		height: 2.5rem;
		display: flex;
		align-items: center;
		justify-content: center;
		border-radius: 0.5rem;
		background: color-mix(in srgb, var(--card-color) 20%, transparent);
	}

	.icon-image {
		width: 1.5rem;
		height: 1.5rem;
		object-fit: contain;
	}

	.icon-emoji {
		font-size: 1.25rem;
	}

	.card-content {
		flex: 1;
		min-width: 0;
	}

	.card-header {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	h3 {
		margin: 0;
		font-size: 0.95rem;
		font-weight: 600;
		color: var(--text-primary, #f9fafb);
	}

	.badge {
		font-size: 0.65rem;
		font-weight: 600;
		padding: 0.1rem 0.4rem;
		border-radius: 0.25rem;
		background: var(--card-color);
		color: #000;
		text-transform: uppercase;
		letter-spacing: 0.03em;
	}

	p {
		margin: 0.25rem 0 0;
		font-size: 0.8rem;
		color: var(--text-secondary, #9ca3af);
		line-height: 1.4;
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/components/SelectionCard.svelte
git commit -m "feat: add reusable SelectionCard component"
```

---

### Task 10: InstallProgress Component

**Files:**
- Create: `apps/web/src/lib/components/InstallProgress.svelte`

Extracts the common SSE install progress pattern used by Forge, Fabric, NeoForge, and Quilt.

- [ ] **Step 1: Create InstallProgress component**

```svelte
<!-- apps/web/src/lib/components/InstallProgress.svelte -->
<script lang="ts">
	import ProgressBar from './ProgressBar.svelte';

	interface Props {
		/** SSE stream URL to connect to */
		streamUrl: string;
		/** Label for the progress display */
		label: string;
		/** Called when install completes successfully */
		oncomplete?: () => void;
		/** Called when install fails */
		onerror?: (error: string) => void;
		/** Called when install is rolled back */
		onrollback?: () => void;
		/** Color for the progress bar */
		color?: 'green' | 'blue' | 'orange' | 'red';
	}

	let {
		streamUrl,
		label,
		oncomplete,
		onerror,
		onrollback,
		color = 'green'
	}: Props = $props();

	let progress = $state(0);
	let currentStep = $state('Starting...');
	let output = $state('');
	let outputExpanded = $state(false);
	let watching = $state(false);
	let completed = $state(false);
	let error = $state('');

	let eventSource: EventSource | null = null;

	function startWatch() {
		if (watching) return;
		watching = true;
		error = '';

		eventSource = new EventSource(streamUrl);
		eventSource.onmessage = (event) => {
			try {
				const data = JSON.parse(event.data);
				progress = data.progress ?? 0;
				currentStep = data.currentStep || 'Installing...';
				if (data.output) output = data.output;

				if (data.status === 'completed') {
					eventSource?.close();
					completed = true;
					oncomplete?.();
				} else if (data.status === 'failed') {
					eventSource?.close();
					error = data.error || 'Installation failed';
					onerror?.(error);
				}
			} catch (err) {
				console.error('Failed to parse install event:', err);
			}
		};
		eventSource.onerror = () => {
			eventSource?.close();
			if (!completed) {
				error = 'Lost connection to install stream';
				onerror?.(error);
			}
		};
	}

	// Auto-start watching when streamUrl is set
	$effect(() => {
		if (streamUrl && !watching) {
			startWatch();
		}
		return () => {
			eventSource?.close();
		};
	});
</script>

<div class="install-progress">
	<div class="progress-header">
		<span class="label">{label}</span>
		<span class="step">{currentStep}</span>
	</div>

	<ProgressBar value={progress} {color} size="md" showLabel />

	{#if error}
		<div class="error-message">
			<span>Error: {error}</span>
		</div>
	{/if}

	{#if output}
		<div class="output-section">
			<button
				class="output-toggle"
				onclick={() => (outputExpanded = !outputExpanded)}
				type="button"
			>
				{outputExpanded ? 'Hide' : 'Show'} output
			</button>
			{#if outputExpanded}
				<pre class="output-log">{output}</pre>
			{/if}
		</div>
	{/if}
</div>

<style>
	.install-progress {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.progress-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
	}

	.label {
		font-weight: 600;
		font-size: 0.9rem;
		color: var(--text-primary, #f9fafb);
	}

	.step {
		font-size: 0.8rem;
		color: var(--text-secondary, #9ca3af);
	}

	.error-message {
		padding: 0.5rem 0.75rem;
		background: rgba(239, 68, 68, 0.1);
		border: 1px solid rgba(239, 68, 68, 0.3);
		border-radius: 0.375rem;
		color: #ef4444;
		font-size: 0.85rem;
	}

	.output-section {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.output-toggle {
		align-self: flex-start;
		background: none;
		border: none;
		color: var(--text-secondary, #9ca3af);
		cursor: pointer;
		font-size: 0.8rem;
		padding: 0;
		text-decoration: underline;
	}

	.output-toggle:hover {
		color: var(--text-primary, #f9fafb);
	}

	.output-log {
		max-height: 300px;
		overflow-y: auto;
		padding: 0.75rem;
		background: #0f172a;
		border-radius: 0.375rem;
		font-size: 0.75rem;
		line-height: 1.5;
		white-space: pre-wrap;
		word-break: break-all;
		margin: 0;
		color: #94a3b8;
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/components/InstallProgress.svelte
git commit -m "feat: add reusable InstallProgress component with SSE streaming"
```

---

### Task 11: TwoColumnVersionPicker Component

**Files:**
- Create: `apps/web/src/lib/components/TwoColumnVersionPicker.svelte`

- [ ] **Step 1: Create TwoColumnVersionPicker component**

```svelte
<!-- apps/web/src/lib/components/TwoColumnVersionPicker.svelte -->
<script lang="ts" generics="TLeft, TRight">
	interface Props {
		/** Left column items */
		leftItems: TLeft[];
		/** Right column items (filtered by left selection) */
		rightItems: TRight[];
		/** Left column label */
		leftLabel: string;
		/** Right column label */
		rightLabel: string;
		/** Get display text for left item */
		leftDisplay: (item: TLeft) => string;
		/** Get display text for right item */
		rightDisplay: (item: TRight) => string;
		/** Get badge text for right item (e.g. "Latest", "Recommended") */
		rightBadge?: (item: TRight) => string | null;
		/** Currently selected left item index */
		selectedLeftIndex: number | null;
		/** Currently selected right item index */
		selectedRightIndex: number | null;
		/** Called when left item is selected */
		onselectleft: (index: number, item: TLeft) => void;
		/** Called when right item is selected */
		onselectright: (index: number, item: TRight) => void;
		/** Loading state */
		loading?: boolean;
		/** Error message */
		error?: string;
	}

	let {
		leftItems,
		rightItems,
		leftLabel,
		rightLabel,
		leftDisplay,
		rightDisplay,
		rightBadge,
		selectedLeftIndex = null,
		selectedRightIndex = null,
		onselectleft,
		onselectright,
		loading = false,
		error = ''
	}: Props = $props();

	let leftSearch = $state('');
	let rightSearch = $state('');

	const filteredLeft = $derived(
		leftSearch.trim()
			? leftItems.filter((item) =>
					leftDisplay(item).toLowerCase().includes(leftSearch.trim().toLowerCase())
				)
			: leftItems
	);

	const filteredRight = $derived(
		rightSearch.trim()
			? rightItems.filter((item) =>
					rightDisplay(item).toLowerCase().includes(rightSearch.trim().toLowerCase())
				)
			: rightItems
	);
</script>

<div class="two-col-picker">
	{#if loading}
		<div class="loading">Loading versions...</div>
	{:else if error}
		<div class="error">{error}</div>
	{:else}
		<div class="column">
			<div class="column-header">
				<span>{leftLabel}</span>
				<input
					type="text"
					placeholder="Search..."
					bind:value={leftSearch}
					class="search-input"
				/>
			</div>
			<div class="column-list">
				{#each filteredLeft as item, i}
					{@const originalIndex = leftItems.indexOf(item)}
					<button
						class="version-item"
						class:selected={selectedLeftIndex === originalIndex}
						onclick={() => onselectleft(originalIndex, item)}
						type="button"
					>
						{leftDisplay(item)}
					</button>
				{/each}
			</div>
		</div>

		<div class="column">
			<div class="column-header">
				<span>{rightLabel}</span>
				{#if rightItems.length > 0}
					<input
						type="text"
						placeholder="Search..."
						bind:value={rightSearch}
						class="search-input"
					/>
				{/if}
			</div>
			<div class="column-list">
				{#if selectedLeftIndex === null}
					<div class="placeholder">Select a {leftLabel.toLowerCase()} first</div>
				{:else if rightItems.length === 0}
					<div class="placeholder">No versions available</div>
				{:else}
					{#each filteredRight as item, i}
						{@const originalIndex = rightItems.indexOf(item)}
						{@const badge = rightBadge?.(item)}
						<button
							class="version-item"
							class:selected={selectedRightIndex === originalIndex}
							onclick={() => onselectright(originalIndex, item)}
							type="button"
						>
							<span>{rightDisplay(item)}</span>
							{#if badge}
								<span class="badge">{badge}</span>
							{/if}
						</button>
					{/each}
				{/if}
			</div>
		</div>
	{/if}
</div>

<style>
	.two-col-picker {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 1rem;
		min-height: 300px;
	}

	.column {
		display: flex;
		flex-direction: column;
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		overflow: hidden;
	}

	.column-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 0.5rem 0.75rem;
		background: var(--header-bg, #111827);
		border-bottom: 1px solid var(--border-color, #374151);
		font-weight: 600;
		font-size: 0.85rem;
	}

	.search-input {
		padding: 0.25rem 0.5rem;
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.25rem;
		background: var(--input-bg, #1f2937);
		color: inherit;
		font-size: 0.8rem;
		width: 120px;
	}

	.column-list {
		flex: 1;
		overflow-y: auto;
		max-height: 400px;
	}

	.version-item {
		display: flex;
		justify-content: space-between;
		align-items: center;
		width: 100%;
		padding: 0.4rem 0.75rem;
		border: none;
		background: transparent;
		color: inherit;
		cursor: pointer;
		font-size: 0.85rem;
		text-align: left;
		font-family: inherit;
	}

	.version-item:hover {
		background: rgba(255, 255, 255, 0.05);
	}

	.version-item.selected {
		background: rgba(59, 130, 246, 0.2);
		color: #60a5fa;
	}

	.badge {
		font-size: 0.65rem;
		font-weight: 600;
		padding: 0.1rem 0.35rem;
		border-radius: 0.2rem;
		background: #22c55e;
		color: #000;
		text-transform: uppercase;
	}

	.loading,
	.error,
	.placeholder {
		grid-column: 1 / -1;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 2rem;
		color: var(--text-secondary, #9ca3af);
		font-size: 0.9rem;
	}

	.error {
		color: #ef4444;
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/components/TwoColumnVersionPicker.svelte
git commit -m "feat: add reusable TwoColumnVersionPicker component"
```

---

## Phase 5: Frontend — Wizard Step Components

### Task 12: CategorySelect Step

**Files:**
- Create: `apps/web/src/routes/(app)/servers/new/steps/CategorySelect.svelte`

- [ ] **Step 1: Create the CategorySelect component**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/steps/CategorySelect.svelte -->
<script lang="ts">
	import SelectionCard from '$lib/components/SelectionCard.svelte';

	export type ServerCategory = 'vanilla' | 'plugins' | 'mods' | 'bedrock' | 'template';

	interface Props {
		onselect: (category: ServerCategory) => void;
	}

	let { onselect }: Props = $props();

	const categories = [
		{
			id: 'vanilla' as const,
			name: 'Vanilla',
			description:
				'The official Minecraft server from Mojang. No mods, no plugins — pure gameplay.',
			icon: '🎮',
			color: '#4ade80'
		},
		{
			id: 'plugins' as const,
			name: 'Plugins',
			description:
				'Enhanced servers with plugin support for custom game modes, anti-cheat, permissions, and more.',
			icon: '🔌',
			color: '#60a5fa'
		},
		{
			id: 'mods' as const,
			name: 'Mods',
			description:
				'Full mod support — new blocks, dimensions, mechanics, and total conversions.',
			icon: '🧩',
			color: '#a855f7'
		},
		{
			id: 'bedrock' as const,
			name: 'Bedrock',
			description:
				'Bedrock Edition dedicated server for cross-platform play (mobile, console, Windows).',
			icon: '🪨',
			color: '#3b82f6'
		},
		{
			id: 'template' as const,
			name: 'Template',
			description: 'Clone an existing server as a starting point.',
			icon: 'T',
			color: '#22d3ee'
		}
	];
</script>

<div class="step">
	<h2>What kind of server?</h2>
	<div class="cards">
		{#each categories as cat}
			<SelectionCard
				title={cat.name}
				description={cat.description}
				icon={cat.icon}
				color={cat.color}
				onclick={() => onselect(cat.id)}
			/>
		{/each}
	</div>
</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.cards {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
mkdir -p apps/web/src/routes/\(app\)/servers/new/steps
git add apps/web/src/routes/\(app\)/servers/new/steps/CategorySelect.svelte
git commit -m "feat: add CategorySelect wizard step"
```

---

### Task 13: ImplementationSelect Step

**Files:**
- Create: `apps/web/src/routes/(app)/servers/new/steps/ImplementationSelect.svelte`

- [ ] **Step 1: Create the ImplementationSelect component**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/steps/ImplementationSelect.svelte -->
<script lang="ts">
	import SelectionCard from '$lib/components/SelectionCard.svelte';
	import type { ServerCategory } from './CategorySelect.svelte';

	export type PluginImpl = 'paper' | 'spigot' | 'craftbukkit';
	export type ModLoader = 'forge' | 'neoforge' | 'fabric' | 'quilt';
	export type Implementation = PluginImpl | ModLoader;

	interface Props {
		category: 'plugins' | 'mods';
		onselect: (impl: Implementation) => void;
		onback: () => void;
	}

	let { category, onselect, onback }: Props = $props();

	const pluginOptions = [
		{
			id: 'paper' as const,
			name: 'Paper',
			description:
				'High-performance Spigot fork with async chunks and extensive optimizations.',
			icon: '📄',
			iconImage: '/images/loaders/paper.png',
			color: '#60a5fa',
			badge: 'Recommended'
		},
		{
			id: 'spigot' as const,
			name: 'Spigot',
			description: 'The original plugin server. Wide compatibility with Bukkit plugins.',
			icon: '🔧',
			color: '#fbbf24'
		},
		{
			id: 'craftbukkit' as const,
			name: 'CraftBukkit',
			description: 'The classic. Fewest modifications to vanilla, built via BuildTools.',
			icon: '🪣',
			color: '#f97316'
		}
	];

	const modOptions = [
		{
			id: 'forge' as const,
			name: 'Forge',
			description:
				'The most established modloader. Largest mod library, widest version support.',
			icon: '🔥',
			iconImage: '/images/loaders/forge.png',
			color: '#ef4444'
		},
		{
			id: 'neoforge' as const,
			name: 'NeoForge',
			description:
				'Community-driven Forge successor. Modern APIs, active development. 1.20.1+ only.',
			icon: '⚡',
			iconImage: '/images/loaders/neoforge.png',
			color: '#f97316'
		},
		{
			id: 'fabric' as const,
			name: 'Fabric',
			description: 'Lightweight and fast. Growing mod ecosystem, popular for newer versions.',
			icon: '🧵',
			iconImage: '/images/loaders/fabric.png',
			color: '#c4b5a4'
		},
		{
			id: 'quilt' as const,
			name: 'Quilt',
			description: 'Fabric-compatible fork with additional mod management features.',
			icon: '🪡',
			iconImage: '/images/loaders/quilt.png',
			color: '#8b5cf6'
		}
	];

	const options = $derived(category === 'plugins' ? pluginOptions : modOptions);
	const heading = $derived(
		category === 'plugins' ? 'Choose a plugin server' : 'Choose a modloader'
	);
</script>

<div class="step">
	<div class="header">
		<button class="back-btn" onclick={onback} type="button">&larr; Back</button>
		<h2>{heading}</h2>
	</div>
	<div class="cards">
		{#each options as opt}
			<SelectionCard
				title={opt.name}
				description={opt.description}
				icon={opt.icon}
				iconImage={opt.iconImage}
				color={opt.color}
				badge={opt.badge}
				onclick={() => onselect(opt.id)}
			/>
		{/each}
	</div>
</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	.header {
		display: flex;
		align-items: center;
		gap: 1rem;
	}

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.back-btn {
		background: none;
		border: 1px solid var(--border-color, #374151);
		color: var(--text-secondary, #9ca3af);
		padding: 0.35rem 0.75rem;
		border-radius: 0.375rem;
		cursor: pointer;
		font-size: 0.85rem;
	}

	.back-btn:hover {
		color: var(--text-primary, #f9fafb);
		border-color: var(--text-secondary, #9ca3af);
	}

	.cards {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/routes/\(app\)/servers/new/steps/ImplementationSelect.svelte
git commit -m "feat: add ImplementationSelect wizard step"
```

---

### Task 14: Version Picker Components

**Files:**
- Create: `apps/web/src/routes/(app)/servers/new/version-pickers/VanillaVersions.svelte`
- Create: `apps/web/src/routes/(app)/servers/new/version-pickers/ForgeVersions.svelte`
- Create: `apps/web/src/routes/(app)/servers/new/version-pickers/NeoForgeVersions.svelte`
- Create: `apps/web/src/routes/(app)/servers/new/version-pickers/FabricVersions.svelte`
- Create: `apps/web/src/routes/(app)/servers/new/version-pickers/QuiltVersions.svelte`
- Create: `apps/web/src/routes/(app)/servers/new/version-pickers/BedrockVersions.svelte`
- Create: `apps/web/src/routes/(app)/servers/new/version-pickers/TemplateSelect.svelte`
- Create: `apps/web/src/routes/(app)/servers/new/steps/VersionSelect.svelte`

This is the largest task. Each version picker wraps TwoColumnVersionPicker or a simple list, and VersionSelect routes to the correct picker based on implementation type.

- [ ] **Step 1: Create VanillaVersions picker**

This handles Vanilla, Paper, Spigot, and CraftBukkit — they all use the same profile-based version list, just filtered by group.

```svelte
<!-- apps/web/src/routes/(app)/servers/new/version-pickers/VanillaVersions.svelte -->
<script lang="ts">
	import type { Profile } from '$lib/api/types';
	import StatusBadge from '$lib/components/StatusBadge.svelte';

	interface Props {
		/** All available profiles from server data */
		profiles: Profile[];
		/** Which implementation to filter for */
		implementation: 'vanilla' | 'paper' | 'spigot' | 'craftbukkit';
		/** Called when a profile is selected */
		onselect: (profile: Profile) => void;
	}

	let { profiles, implementation, onselect }: Props = $props();

	let search = $state('');
	let showSnapshots = $state(false);
	let page = $state(0);
	const perPage = 20;

	const filtered = $derived.by(() => {
		let result = profiles.filter((p) => {
			switch (implementation) {
				case 'vanilla':
					return p.group === 'vanilla';
				case 'paper':
					return p.group === 'paper';
				case 'spigot':
					return p.group === 'spigot' || (p.group === 'vanilla' && p.type === 'release');
				case 'craftbukkit':
					return (
						p.group === 'craftbukkit' ||
						p.group === 'bukkit' ||
						(p.group === 'vanilla' && p.type === 'release')
					);
				default:
					return false;
			}
		});
		if (!showSnapshots) {
			result = result.filter((p) => p.type === 'release' || p.type === 'latest');
		}
		if (search.trim()) {
			result = result.filter((p) =>
				p.version.toLowerCase().includes(search.trim().toLowerCase())
			);
		}
		return result;
	});

	const paged = $derived(filtered.slice(page * perPage, (page + 1) * perPage));
	const totalPages = $derived(Math.ceil(filtered.length / perPage));

	// Reset page when filter changes
	$effect(() => {
		search;
		showSnapshots;
		page = 0;
	});
</script>

<div class="version-picker">
	<div class="controls">
		<input type="text" placeholder="Search versions..." bind:value={search} class="search" />
		{#if implementation === 'vanilla'}
			<label class="toggle">
				<input type="checkbox" bind:checked={showSnapshots} />
				Show snapshots
			</label>
		{/if}
	</div>

	<div class="version-list">
		{#each paged as profile}
			<button class="version-row" onclick={() => onselect(profile)} type="button">
				<span class="version-name">{profile.version}</span>
				<span class="version-meta">
					{#if profile.downloaded}
						<StatusBadge variant="success" size="sm">Ready</StatusBadge>
					{:else}
						<StatusBadge variant="info" size="sm">Will Download</StatusBadge>
					{/if}
				</span>
			</button>
		{/each}
		{#if paged.length === 0}
			<div class="empty">No versions found</div>
		{/if}
	</div>

	{#if totalPages > 1}
		<div class="pagination">
			<button onclick={() => (page = Math.max(0, page - 1))} disabled={page === 0} type="button"
				>Prev</button
			>
			<span>
				Page {page + 1} of {totalPages}
			</span>
			<button
				onclick={() => (page = Math.min(totalPages - 1, page + 1))}
				disabled={page >= totalPages - 1}
				type="button">Next</button
			>
		</div>
	{/if}
</div>

<style>
	.version-picker {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.controls {
		display: flex;
		gap: 1rem;
		align-items: center;
	}

	.search {
		flex: 1;
		padding: 0.4rem 0.75rem;
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.375rem;
		background: var(--input-bg, #1f2937);
		color: inherit;
		font-size: 0.85rem;
	}

	.toggle {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		font-size: 0.8rem;
		color: var(--text-secondary, #9ca3af);
		cursor: pointer;
		white-space: nowrap;
	}

	.version-list {
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		overflow: hidden;
		max-height: 400px;
		overflow-y: auto;
	}

	.version-row {
		display: flex;
		justify-content: space-between;
		align-items: center;
		width: 100%;
		padding: 0.5rem 0.75rem;
		border: none;
		border-bottom: 1px solid var(--border-color, #374151);
		background: transparent;
		color: inherit;
		cursor: pointer;
		font-size: 0.85rem;
		text-align: left;
		font-family: inherit;
	}

	.version-row:last-child {
		border-bottom: none;
	}

	.version-row:hover {
		background: rgba(255, 255, 255, 0.05);
	}

	.empty {
		padding: 2rem;
		text-align: center;
		color: var(--text-secondary, #9ca3af);
	}

	.pagination {
		display: flex;
		justify-content: center;
		align-items: center;
		gap: 1rem;
		font-size: 0.85rem;
	}

	.pagination button {
		padding: 0.3rem 0.75rem;
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.25rem;
		background: transparent;
		color: inherit;
		cursor: pointer;
		font-size: 0.8rem;
	}

	.pagination button:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}
</style>
```

- [ ] **Step 2: Create ForgeVersions picker**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/version-pickers/ForgeVersions.svelte -->
<script lang="ts">
	import * as api from '$lib/api/client';
	import type { ForgeVersion } from '$lib/api/types';
	import TwoColumnVersionPicker from '$lib/components/TwoColumnVersionPicker.svelte';

	interface Props {
		onselect: (mcVersion: string, forgeVersion: ForgeVersion) => void;
	}

	let { onselect }: Props = $props();

	let versions = $state<ForgeVersion[]>([]);
	let loading = $state(true);
	let error = $state('');
	let selectedMcIndex = $state<number | null>(null);
	let selectedForgeIndex = $state<number | null>(null);

	const mcVersions = $derived.by(() => {
		const unique = [...new Set(versions.map((v) => v.minecraftVersion))];
		return unique.sort((a, b) => {
			const ap = a.split('.').map(Number);
			const bp = b.split('.').map(Number);
			for (let i = 0; i < Math.max(ap.length, bp.length); i++) {
				if ((bp[i] ?? 0) !== (ap[i] ?? 0)) return (bp[i] ?? 0) - (ap[i] ?? 0);
			}
			return 0;
		});
	});

	const forgeForMc = $derived.by(() => {
		if (selectedMcIndex === null) return [];
		const mc = mcVersions[selectedMcIndex];
		return versions.filter((v) => v.minecraftVersion === mc);
	});

	async function load() {
		loading = true;
		error = '';
		const result = await api.getForgeVersions(fetch);
		if (result.error) {
			error = result.error;
		} else if (result.data) {
			versions = result.data;
		}
		loading = false;
	}

	load();
</script>

<TwoColumnVersionPicker
	leftItems={mcVersions}
	rightItems={forgeForMc}
	leftLabel="Minecraft Version"
	rightLabel="Forge Build"
	leftDisplay={(v) => v}
	rightDisplay={(v) => v.forgeVersion}
	rightBadge={(v) => (v.isRecommended ? 'Recommended' : v.isLatest ? 'Latest' : null)}
	{selectedMcIndex}
	selectedLeftIndex={selectedMcIndex}
	selectedRightIndex={selectedForgeIndex}
	onselectleft={(i, _item) => {
		selectedMcIndex = i;
		selectedForgeIndex = null;
		// Auto-select recommended
		const mc = mcVersions[i];
		const recommended = versions.findIndex(
			(v) => v.minecraftVersion === mc && v.isRecommended
		);
		if (recommended >= 0) {
			const forgeList = versions.filter((v) => v.minecraftVersion === mc);
			const recIdx = forgeList.findIndex((v) => v.isRecommended);
			if (recIdx >= 0) {
				selectedForgeIndex = recIdx;
				onselect(mc, forgeList[recIdx]);
			}
		}
	}}
	onselectright={(i, item) => {
		selectedForgeIndex = i;
		if (selectedMcIndex !== null) {
			onselect(mcVersions[selectedMcIndex], item);
		}
	}}
	{loading}
	{error}
/>
```

- [ ] **Step 3: Create NeoForgeVersions picker**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/version-pickers/NeoForgeVersions.svelte -->
<script lang="ts">
	import * as api from '$lib/api/client';
	import type { NeoForgeVersion } from '$lib/api/types';
	import TwoColumnVersionPicker from '$lib/components/TwoColumnVersionPicker.svelte';

	interface Props {
		onselect: (mcVersion: string, neoForgeVersion: NeoForgeVersion) => void;
	}

	let { onselect }: Props = $props();

	let versions = $state<NeoForgeVersion[]>([]);
	let loading = $state(true);
	let error = $state('');
	let selectedMcIndex = $state<number | null>(null);
	let selectedNfIndex = $state<number | null>(null);

	const mcVersions = $derived.by(() => {
		const unique = [...new Set(versions.map((v) => v.minecraftVersion))];
		return unique.sort((a, b) => {
			const ap = a.split('.').map(Number);
			const bp = b.split('.').map(Number);
			for (let i = 0; i < Math.max(ap.length, bp.length); i++) {
				if ((bp[i] ?? 0) !== (ap[i] ?? 0)) return (bp[i] ?? 0) - (ap[i] ?? 0);
			}
			return 0;
		});
	});

	const nfForMc = $derived.by(() => {
		if (selectedMcIndex === null) return [];
		const mc = mcVersions[selectedMcIndex];
		return versions.filter((v) => v.minecraftVersion === mc);
	});

	async function load() {
		loading = true;
		error = '';
		const result = await api.getNeoForgeVersions(fetch);
		if (result.error) {
			error = result.error;
		} else if (result.data) {
			versions = result.data;
		}
		loading = false;
	}

	load();
</script>

<TwoColumnVersionPicker
	leftItems={mcVersions}
	rightItems={nfForMc}
	leftLabel="Minecraft Version"
	rightLabel="NeoForge Build"
	leftDisplay={(v) => v}
	rightDisplay={(v) => v.neoForgeVersion}
	rightBadge={(v) => (v.isLatest ? 'Latest' : null)}
	selectedLeftIndex={selectedMcIndex}
	selectedRightIndex={selectedNfIndex}
	onselectleft={(i, _item) => {
		selectedMcIndex = i;
		selectedNfIndex = null;
		// Auto-select latest
		const mc = mcVersions[i];
		const nfList = versions.filter((v) => v.minecraftVersion === mc);
		const latestIdx = nfList.findIndex((v) => v.isLatest);
		if (latestIdx >= 0) {
			selectedNfIndex = latestIdx;
			onselect(mc, nfList[latestIdx]);
		}
	}}
	onselectright={(i, item) => {
		selectedNfIndex = i;
		if (selectedMcIndex !== null) {
			onselect(mcVersions[selectedMcIndex], item);
		}
	}}
	{loading}
	{error}
/>
```

- [ ] **Step 4: Create FabricVersions picker**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/version-pickers/FabricVersions.svelte -->
<script lang="ts">
	import * as api from '$lib/api/client';
	import type { FabricGameVersion, FabricLoaderVersion } from '$lib/api/types';
	import TwoColumnVersionPicker from '$lib/components/TwoColumnVersionPicker.svelte';

	interface Props {
		onselect: (mcVersion: string, loaderVersion: string) => void;
	}

	let { onselect }: Props = $props();

	let gameVersions = $state<FabricGameVersion[]>([]);
	let loaderVersions = $state<FabricLoaderVersion[]>([]);
	let loading = $state(true);
	let error = $state('');
	let selectedMcIndex = $state<number | null>(null);
	let selectedLoaderIndex = $state<number | null>(null);

	const stableGameVersions = $derived(gameVersions.filter((v) => v.isStable));
	const stableLoaderVersions = $derived(loaderVersions.filter((v) => v.isStable));

	async function load() {
		loading = true;
		error = '';
		const [gameResult, loaderResult] = await Promise.all([
			api.getFabricGameVersions(fetch),
			api.getFabricLoaderVersions(fetch)
		]);
		if (gameResult.error) error = gameResult.error;
		else if (gameResult.data) gameVersions = gameResult.data;
		if (loaderResult.error) error = loaderResult.error;
		else if (loaderResult.data) loaderVersions = loaderResult.data;
		loading = false;
	}

	load();
</script>

<TwoColumnVersionPicker
	leftItems={stableGameVersions}
	rightItems={stableLoaderVersions}
	leftLabel="Minecraft Version"
	rightLabel="Loader Version"
	leftDisplay={(v) => v.version}
	rightDisplay={(v) => v.version}
	rightBadge={(_v) => null}
	selectedLeftIndex={selectedMcIndex}
	selectedRightIndex={selectedLoaderIndex}
	onselectleft={(i, item) => {
		selectedMcIndex = i;
		// Auto-select first stable loader
		if (stableLoaderVersions.length > 0) {
			selectedLoaderIndex = 0;
			onselect(item.version, stableLoaderVersions[0].version);
		}
	}}
	onselectright={(i, item) => {
		selectedLoaderIndex = i;
		if (selectedMcIndex !== null) {
			onselect(stableGameVersions[selectedMcIndex].version, item.version);
		}
	}}
	{loading}
	{error}
/>
```

- [ ] **Step 5: Create QuiltVersions picker**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/version-pickers/QuiltVersions.svelte -->
<script lang="ts">
	import * as api from '$lib/api/client';
	import type { QuiltGameVersion, QuiltLoaderVersion } from '$lib/api/types';
	import TwoColumnVersionPicker from '$lib/components/TwoColumnVersionPicker.svelte';

	interface Props {
		onselect: (mcVersion: string, loaderVersion: string) => void;
	}

	let { onselect }: Props = $props();

	let gameVersions = $state<QuiltGameVersion[]>([]);
	let loaderVersions = $state<QuiltLoaderVersion[]>([]);
	let loading = $state(true);
	let error = $state('');
	let selectedMcIndex = $state<number | null>(null);
	let selectedLoaderIndex = $state<number | null>(null);

	const stableGameVersions = $derived(gameVersions.filter((v) => v.isStable));
	const stableLoaderVersions = $derived(loaderVersions.filter((v) => v.isStable));

	async function load() {
		loading = true;
		error = '';
		const [gameResult, loaderResult] = await Promise.all([
			api.getQuiltGameVersions(fetch),
			api.getQuiltLoaderVersions(fetch)
		]);
		if (gameResult.error) error = gameResult.error;
		else if (gameResult.data) gameVersions = gameResult.data;
		if (loaderResult.error) error = loaderResult.error;
		else if (loaderResult.data) loaderVersions = loaderResult.data;
		loading = false;
	}

	load();
</script>

<TwoColumnVersionPicker
	leftItems={stableGameVersions}
	rightItems={stableLoaderVersions}
	leftLabel="Minecraft Version"
	rightLabel="Loader Version"
	leftDisplay={(v) => v.version}
	rightDisplay={(v) => v.version}
	rightBadge={(_v) => null}
	selectedLeftIndex={selectedMcIndex}
	selectedRightIndex={selectedLoaderIndex}
	onselectleft={(i, item) => {
		selectedMcIndex = i;
		if (stableLoaderVersions.length > 0) {
			selectedLoaderIndex = 0;
			onselect(item.version, stableLoaderVersions[0].version);
		}
	}}
	onselectright={(i, item) => {
		selectedLoaderIndex = i;
		if (selectedMcIndex !== null) {
			onselect(stableGameVersions[selectedMcIndex].version, item.version);
		}
	}}
	{loading}
	{error}
/>
```

- [ ] **Step 6: Create BedrockVersions picker**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/version-pickers/BedrockVersions.svelte -->
<script lang="ts">
	import type { Profile } from '$lib/api/types';
	import StatusBadge from '$lib/components/StatusBadge.svelte';

	interface Props {
		profiles: Profile[];
		onselect: (profile: Profile) => void;
	}

	let { profiles, onselect }: Props = $props();

	let showPreview = $state(false);

	const filtered = $derived.by(() => {
		return profiles.filter((p) => {
			if (showPreview) return p.group === 'bedrock-server' || p.group === 'bedrock-server-preview';
			return p.group === 'bedrock-server';
		});
	});
</script>

<div class="version-picker">
	<div class="controls">
		<label class="toggle">
			<input type="checkbox" bind:checked={showPreview} />
			Show preview builds
		</label>
	</div>

	<div class="version-list">
		{#each filtered as profile}
			<button class="version-row" onclick={() => onselect(profile)} type="button">
				<span>{profile.version}</span>
				<span>
					{#if profile.group === 'bedrock-server-preview'}
						<StatusBadge variant="warning" size="sm">Preview</StatusBadge>
					{:else if profile.downloaded}
						<StatusBadge variant="success" size="sm">Ready</StatusBadge>
					{:else}
						<StatusBadge variant="info" size="sm">Will Download</StatusBadge>
					{/if}
				</span>
			</button>
		{/each}
	</div>
</div>

<style>
	.version-picker {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.controls {
		display: flex;
		gap: 1rem;
	}

	.toggle {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		font-size: 0.8rem;
		color: var(--text-secondary, #9ca3af);
		cursor: pointer;
	}

	.version-list {
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		overflow: hidden;
		max-height: 400px;
		overflow-y: auto;
	}

	.version-row {
		display: flex;
		justify-content: space-between;
		align-items: center;
		width: 100%;
		padding: 0.5rem 0.75rem;
		border: none;
		border-bottom: 1px solid var(--border-color, #374151);
		background: transparent;
		color: inherit;
		cursor: pointer;
		font-size: 0.85rem;
		text-align: left;
		font-family: inherit;
	}

	.version-row:last-child {
		border-bottom: none;
	}

	.version-row:hover {
		background: rgba(255, 255, 255, 0.05);
	}
</style>
```

- [ ] **Step 7: Create TemplateSelect picker**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/version-pickers/TemplateSelect.svelte -->
<script lang="ts">
	import type { ServerSummary } from '$lib/api/types';

	interface Props {
		servers: ServerSummary[];
		onselect: (serverName: string) => void;
	}

	let { servers, onselect }: Props = $props();
</script>

<div class="template-picker">
	<p class="hint">Select an existing server to clone as your starting point:</p>
	<div class="server-list">
		{#each servers as server}
			<button class="server-row" onclick={() => onselect(server.name)} type="button">
				<span class="server-name">{server.name}</span>
				<span class="server-type">{server.serverType}</span>
			</button>
		{:else}
			<div class="empty">No existing servers to clone</div>
		{/each}
	</div>
</div>

<style>
	.template-picker {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.hint {
		margin: 0;
		font-size: 0.85rem;
		color: var(--text-secondary, #9ca3af);
	}

	.server-list {
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		overflow: hidden;
		max-height: 400px;
		overflow-y: auto;
	}

	.server-row {
		display: flex;
		justify-content: space-between;
		align-items: center;
		width: 100%;
		padding: 0.5rem 0.75rem;
		border: none;
		border-bottom: 1px solid var(--border-color, #374151);
		background: transparent;
		color: inherit;
		cursor: pointer;
		font-size: 0.85rem;
		text-align: left;
		font-family: inherit;
	}

	.server-row:last-child {
		border-bottom: none;
	}

	.server-row:hover {
		background: rgba(255, 255, 255, 0.05);
	}

	.server-type {
		color: var(--text-secondary, #9ca3af);
		font-size: 0.75rem;
	}

	.empty {
		padding: 2rem;
		text-align: center;
		color: var(--text-secondary, #9ca3af);
	}
</style>
```

- [ ] **Step 8: Create VersionSelect routing component**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/steps/VersionSelect.svelte -->
<script lang="ts">
	import type { Profile, ServerSummary, ForgeVersion } from '$lib/api/types';
	import type { NeoForgeVersion } from '$lib/api/types';
	import type { Implementation } from './ImplementationSelect.svelte';
	import VanillaVersions from '../version-pickers/VanillaVersions.svelte';
	import ForgeVersions from '../version-pickers/ForgeVersions.svelte';
	import NeoForgeVersions from '../version-pickers/NeoForgeVersions.svelte';
	import FabricVersions from '../version-pickers/FabricVersions.svelte';
	import QuiltVersions from '../version-pickers/QuiltVersions.svelte';
	import BedrockVersions from '../version-pickers/BedrockVersions.svelte';
	import TemplateSelect from '../version-pickers/TemplateSelect.svelte';

	interface VersionSelection {
		profileId?: string;
		minecraftVersion?: string;
		loaderVersion?: string;
		forgeVersion?: ForgeVersion;
		neoForgeVersion?: NeoForgeVersion;
		cloneSource?: string;
	}

	interface Props {
		implementation: Implementation | 'vanilla' | 'bedrock' | 'template';
		profiles: Profile[];
		servers: ServerSummary[];
		onselect: (selection: VersionSelection) => void;
		onback: () => void;
	}

	let { implementation, profiles, servers, onselect, onback }: Props = $props();

	const labels: Record<string, string> = {
		vanilla: 'Vanilla',
		paper: 'Paper',
		spigot: 'Spigot',
		craftbukkit: 'CraftBukkit',
		forge: 'Forge',
		neoforge: 'NeoForge',
		fabric: 'Fabric',
		quilt: 'Quilt',
		bedrock: 'Bedrock',
		template: 'Template'
	};
</script>

<div class="step">
	<div class="header">
		<button class="back-btn" onclick={onback} type="button">&larr; Back</button>
		<h2>Select {labels[implementation]} version</h2>
	</div>

	{#if implementation === 'vanilla' || implementation === 'paper' || implementation === 'spigot' || implementation === 'craftbukkit'}
		<VanillaVersions
			{profiles}
			{implementation}
			onselect={(profile) => onselect({ profileId: profile.id, minecraftVersion: profile.version })}
		/>
	{:else if implementation === 'forge'}
		<ForgeVersions
			onselect={(mc, forge) =>
				onselect({ minecraftVersion: mc, forgeVersion: forge })}
		/>
	{:else if implementation === 'neoforge'}
		<NeoForgeVersions
			onselect={(mc, nf) =>
				onselect({ minecraftVersion: mc, neoForgeVersion: nf })}
		/>
	{:else if implementation === 'fabric'}
		<FabricVersions
			onselect={(mc, loader) =>
				onselect({ minecraftVersion: mc, loaderVersion: loader })}
		/>
	{:else if implementation === 'quilt'}
		<QuiltVersions
			onselect={(mc, loader) =>
				onselect({ minecraftVersion: mc, loaderVersion: loader })}
		/>
	{:else if implementation === 'bedrock'}
		<BedrockVersions
			{profiles}
			onselect={(profile) => onselect({ profileId: profile.id, minecraftVersion: profile.version })}
		/>
	{:else if implementation === 'template'}
		<TemplateSelect
			{servers}
			onselect={(name) => onselect({ cloneSource: name })}
		/>
	{/if}
</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	.header {
		display: flex;
		align-items: center;
		gap: 1rem;
	}

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.back-btn {
		background: none;
		border: 1px solid var(--border-color, #374151);
		color: var(--text-secondary, #9ca3af);
		padding: 0.35rem 0.75rem;
		border-radius: 0.375rem;
		cursor: pointer;
		font-size: 0.85rem;
	}

	.back-btn:hover {
		color: var(--text-primary, #f9fafb);
		border-color: var(--text-secondary, #9ca3af);
	}
</style>
```

- [ ] **Step 9: Commit all version pickers**

```bash
mkdir -p apps/web/src/routes/\(app\)/servers/new/version-pickers
git add apps/web/src/routes/\(app\)/servers/new/version-pickers/ apps/web/src/routes/\(app\)/servers/new/steps/VersionSelect.svelte
git commit -m "feat: add version picker components and VersionSelect routing step"
```

---

### Task 15: ServerName and Creating Steps

**Files:**
- Create: `apps/web/src/routes/(app)/servers/new/steps/ServerName.svelte`
- Create: `apps/web/src/routes/(app)/servers/new/steps/Creating.svelte`

- [ ] **Step 1: Create ServerName step**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/steps/ServerName.svelte -->
<script lang="ts">
	interface Props {
		value: string;
		error: string;
		onchange: (name: string) => void;
		oncreate: () => void;
		onback: () => void;
	}

	let { value, error, onchange, oncreate, onback }: Props = $props();

	const namePattern = /^[a-zA-Z0-9][a-zA-Z0-9 _\-\.]{0,63}$/;
	const isValid = $derived(namePattern.test(value.trim()) && !value.includes('..'));
</script>

<div class="step">
	<div class="header">
		<button class="back-btn" onclick={onback} type="button">&larr; Back</button>
		<h2>Name your server</h2>
	</div>

	<div class="name-input-group">
		<input
			type="text"
			placeholder="my-server"
			value={value}
			oninput={(e) => onchange(e.currentTarget.value)}
			class="name-input"
			class:invalid={value.trim() && !isValid}
		/>
		{#if value.trim() && !isValid}
			<p class="validation-error">
				Name must start with a letter or number, and contain only letters, numbers, spaces,
				hyphens, underscores, or dots. Max 64 characters.
			</p>
		{/if}
		{#if error}
			<p class="validation-error">{error}</p>
		{/if}
	</div>

	<button class="create-btn" onclick={oncreate} disabled={!isValid || !value.trim()} type="button">
		Create Server
	</button>
</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	.header {
		display: flex;
		align-items: center;
		gap: 1rem;
	}

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.back-btn {
		background: none;
		border: 1px solid var(--border-color, #374151);
		color: var(--text-secondary, #9ca3af);
		padding: 0.35rem 0.75rem;
		border-radius: 0.375rem;
		cursor: pointer;
		font-size: 0.85rem;
	}

	.back-btn:hover {
		color: var(--text-primary, #f9fafb);
		border-color: var(--text-secondary, #9ca3af);
	}

	.name-input-group {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.name-input {
		padding: 0.6rem 0.75rem;
		border: 2px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		background: var(--input-bg, #1f2937);
		color: inherit;
		font-size: 1rem;
	}

	.name-input:focus {
		outline: none;
		border-color: #3b82f6;
	}

	.name-input.invalid {
		border-color: #ef4444;
	}

	.validation-error {
		margin: 0;
		font-size: 0.8rem;
		color: #ef4444;
	}

	.create-btn {
		padding: 0.6rem 1.5rem;
		border: none;
		border-radius: 0.5rem;
		background: #3b82f6;
		color: white;
		font-size: 0.95rem;
		font-weight: 600;
		cursor: pointer;
		align-self: flex-start;
	}

	.create-btn:hover:not(:disabled) {
		background: #2563eb;
	}

	.create-btn:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}
</style>
```

- [ ] **Step 2: Create Creating step**

```svelte
<!-- apps/web/src/routes/(app)/servers/new/steps/Creating.svelte -->
<script lang="ts">
	import InstallProgress from '$lib/components/InstallProgress.svelte';
	import ProgressBar from '$lib/components/ProgressBar.svelte';

	interface Props {
		/** Implementation being installed */
		implementation: string;
		/** Server name being created */
		serverName: string;
		/** SSE stream URL (for modloader installs) */
		streamUrl?: string;
		/** Simple progress value (for profile downloads) */
		progress?: number;
		/** Simple step text (for profile downloads) */
		stepText?: string;
		/** Whether creation is complete */
		completed: boolean;
		/** Error if creation failed */
		error?: string;
		/** Navigate to the created server */
		onviewserver: () => void;
	}

	let {
		implementation,
		serverName,
		streamUrl,
		progress,
		stepText,
		completed,
		error,
		onviewserver
	}: Props = $props();

	const label = $derived(`Installing ${implementation} server "${serverName}"`);
</script>

<div class="step">
	<h2>Creating server...</h2>

	{#if streamUrl}
		<InstallProgress {streamUrl} {label} />
	{:else}
		<div class="simple-progress">
			<p class="step-text">{stepText || 'Creating server...'}</p>
			<ProgressBar value={progress ?? 0} color="green" size="md" showLabel />
		</div>
	{/if}

	{#if error}
		<div class="error">{error}</div>
	{/if}

	{#if completed}
		<div class="completed">
			<p>Server created successfully!</p>
			<button class="view-btn" onclick={onviewserver} type="button">View Server</button>
		</div>
	{/if}
</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.step-text {
		margin: 0 0 0.5rem;
		font-size: 0.85rem;
		color: var(--text-secondary, #9ca3af);
	}

	.error {
		padding: 0.75rem;
		background: rgba(239, 68, 68, 0.1);
		border: 1px solid rgba(239, 68, 68, 0.3);
		border-radius: 0.375rem;
		color: #ef4444;
	}

	.completed {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 1rem;
		padding: 1.5rem;
		background: rgba(34, 197, 94, 0.1);
		border: 1px solid rgba(34, 197, 94, 0.3);
		border-radius: 0.5rem;
	}

	.completed p {
		margin: 0;
		font-weight: 600;
		color: #22c55e;
	}

	.view-btn {
		padding: 0.5rem 1.25rem;
		border: none;
		border-radius: 0.375rem;
		background: #22c55e;
		color: #000;
		font-weight: 600;
		cursor: pointer;
	}

	.view-btn:hover {
		background: #16a34a;
	}
</style>
```

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/routes/\(app\)/servers/new/steps/ServerName.svelte apps/web/src/routes/\(app\)/servers/new/steps/Creating.svelte
git commit -m "feat: add ServerName and Creating wizard step components"
```

---

### Task 16: Wizard Shell Rewrite

**Files:**
- Modify: `apps/web/src/routes/(app)/servers/new/+page.svelte` (complete rewrite)

This is the main orchestration component. It replaces the 1800-line monolith with a compact shell that manages state transitions and delegates to step components.

**Note:** The existing file at `+page.svelte` will be completely replaced. Before writing, read it to confirm nothing is missed. The existing `+page.server.ts` stays unchanged — it already loads profiles and servers.

- [ ] **Step 1: Read the existing +page.server.ts to confirm data shape**

Run: `cat apps/web/src/routes/\(app\)/servers/new/+page.server.ts`
Expected: Loads profiles and servers — confirms `data.profiles` and `data.servers` available.

- [ ] **Step 2: Rewrite +page.svelte as wizard shell**

Replace the entire file with:

```svelte
<!-- apps/web/src/routes/(app)/servers/new/+page.svelte -->
<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import type { PageData } from './$types';
	import type { ForgeVersion } from '$lib/api/types';
	import type { NeoForgeVersion } from '$lib/api/types';
	import CategorySelect, { type ServerCategory } from './steps/CategorySelect.svelte';
	import ImplementationSelect, { type Implementation } from './steps/ImplementationSelect.svelte';
	import VersionSelect from './steps/VersionSelect.svelte';
	import ServerName from './steps/ServerName.svelte';
	import Creating from './steps/Creating.svelte';

	let { data }: { data: PageData } = $props();

	// Wizard state
	type WizardStep = 'category' | 'implementation' | 'version' | 'name' | 'creating';
	let step = $state<WizardStep>('category');

	let category = $state<ServerCategory | null>(null);
	let implementation = $state<Implementation | 'vanilla' | 'bedrock' | 'template' | null>(null);
	let serverName = $state('');
	let createError = $state('');

	// Version selection state
	let selectedProfileId = $state('');
	let selectedMcVersion = $state('');
	let selectedLoaderVersion = $state('');
	let selectedForgeVersion = $state<ForgeVersion | null>(null);
	let selectedNeoForgeVersion = $state<NeoForgeVersion | null>(null);
	let cloneSource = $state('');

	// BuildTools state (for Spigot/CraftBukkit)
	let buildToolsRunId = $state('');

	// Creating state
	let installStreamUrl = $state('');
	let simpleProgress = $state(0);
	let simpleStepText = $state('');
	let createCompleted = $state(false);

	function selectCategory(cat: ServerCategory) {
		category = cat;
		// Categories that skip implementation selection
		if (cat === 'vanilla') {
			implementation = 'vanilla';
			step = 'version';
		} else if (cat === 'bedrock') {
			implementation = 'bedrock';
			step = 'version';
		} else if (cat === 'template') {
			implementation = 'template';
			step = 'version';
		} else {
			step = 'implementation';
		}
	}

	function selectImplementation(impl: Implementation) {
		implementation = impl;
		step = 'version';
	}

	function selectVersion(selection: Record<string, any>) {
		if (selection.profileId) selectedProfileId = selection.profileId;
		if (selection.minecraftVersion) selectedMcVersion = selection.minecraftVersion;
		if (selection.loaderVersion) selectedLoaderVersion = selection.loaderVersion;
		if (selection.forgeVersion) selectedForgeVersion = selection.forgeVersion;
		if (selection.neoForgeVersion) selectedNeoForgeVersion = selection.neoForgeVersion;
		if (selection.cloneSource) cloneSource = selection.cloneSource;
		step = 'name';
	}

	function goBackFromImpl() {
		step = 'category';
		category = null;
		implementation = null;
	}

	function goBackFromVersion() {
		if (category === 'plugins' || category === 'mods') {
			step = 'implementation';
			implementation = null;
		} else {
			step = 'category';
			category = null;
			implementation = null;
		}
	}

	function goBackFromName() {
		step = 'version';
	}

	async function createServer() {
		createError = '';
		const name = serverName.trim();
		if (!name || !implementation) return;

		step = 'creating';

		// Handle template/clone
		if (implementation === 'template' && cloneSource) {
			simpleStepText = 'Cloning server...';
			simpleProgress = 10;
			const result = await api.cloneServer(fetch, cloneSource, { newName: name });
			if (result.error) {
				createError = result.error;
				return;
			}
			simpleProgress = 100;
			createCompleted = true;
			return;
		}

		// Create the server first
		simpleStepText = 'Creating server...';
		simpleProgress = 5;
		const serverType = implementation === 'bedrock' ? 'bedrock' : 'java';
		const createResult = await api.createServer(fetch, {
			name,
			ownerUid: 1000,
			ownerGid: 1000,
			serverType
		});
		if (createResult.error) {
			createError = createResult.error;
			return;
		}

		// For modloaders, trigger installation
		if (implementation === 'forge' && selectedForgeVersion) {
			const result = await api.installForge(
				fetch,
				selectedMcVersion,
				selectedForgeVersion.forgeVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/forge/install/${result.data.installId}/stream`;
			}
		} else if (implementation === 'neoforge' && selectedNeoForgeVersion) {
			const result = await api.installNeoForge(
				fetch,
				selectedMcVersion,
				selectedNeoForgeVersion.neoForgeVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/neoforge/install/${result.data.installId}/stream`;
			}
		} else if (implementation === 'fabric') {
			const result = await api.installFabric(
				fetch,
				selectedMcVersion,
				selectedLoaderVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/fabric/install/${result.data.installId}/stream`;
			}
		} else if (implementation === 'quilt') {
			const result = await api.installQuilt(
				fetch,
				selectedMcVersion,
				selectedLoaderVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/quilt/install/${result.data.installId}/stream`;
			}
		} else if (selectedProfileId) {
			const needsBuildTools = implementation === 'spigot' || implementation === 'craftbukkit';

			if (needsBuildTools) {
				// Spigot/CraftBukkit require BuildTools to compile the server JAR
				const selectedProfile = data.profiles.data?.find((p) => p.id === selectedProfileId);
				const version = selectedProfile?.version ?? selectedMcVersion;
				// The BuildTools output profile ID follows this pattern
				selectedProfileId = `${implementation}-${version}`;

				const btResponse = await fetch('/api/host/profiles/buildtools', {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({ group: implementation, version })
				});

				if (btResponse.ok) {
					const btResult = await btResponse.json();
					buildToolsRunId = btResult.runId;
					installStreamUrl = `/api/host/buildtools/${btResult.runId}/stream`;
				} else {
					const err = await btResponse.json().catch(() => ({ error: 'Failed to start BuildTools' }));
					createError = err.error || 'Failed to start BuildTools';
				}
				return;
			}

			// Profile-based install (vanilla, paper, bedrock)
			simpleStepText = 'Downloading and configuring...';
			simpleProgress = 20;

			const profile = data.profiles.data?.find((p) => p.id === selectedProfileId);
			if (profile && !profile.downloaded) {
				const dlResult = await api.downloadProfile(fetch, selectedProfileId);
				if (dlResult.error) {
					createError = dlResult.error;
					return;
				}
			}

			simpleProgress = 60;
			simpleStepText = 'Copying server files...';

			const copyResult = await api.copyProfileToServer(fetch, selectedProfileId, name);
			if (copyResult.error) {
				createError = copyResult.error;
				return;
			}

			simpleProgress = 100;
			createCompleted = true;
			return;
		}

		// For modloaders, completion is handled by InstallProgress component
		if (!installStreamUrl) {
			simpleProgress = 100;
			createCompleted = true;
		}
	}

	function viewServer() {
		goto(`/servers/${encodeURIComponent(serverName.trim())}`);
	}
</script>

<svelte:head>
	<title>New Server | MineOS</title>
</svelte:head>

<div class="wizard">
	<div class="wizard-container">
		{#if step === 'category'}
			<CategorySelect onselect={selectCategory} />
		{:else if step === 'implementation' && (category === 'plugins' || category === 'mods')}
			<ImplementationSelect
				{category}
				onselect={selectImplementation}
				onback={goBackFromImpl}
			/>
		{:else if step === 'version' && implementation}
			<VersionSelect
				{implementation}
				profiles={data.profiles.data ?? []}
				servers={data.servers.data ?? []}
				onselect={selectVersion}
				onback={goBackFromVersion}
			/>
		{:else if step === 'name'}
			<ServerName
				value={serverName}
				error={createError}
				onchange={(v) => (serverName = v)}
				oncreate={createServer}
				onback={goBackFromName}
			/>
		{:else if step === 'creating'}
			<Creating
				implementation={implementation ?? 'unknown'}
				serverName={serverName}
				streamUrl={installStreamUrl || undefined}
				progress={simpleProgress}
				stepText={simpleStepText}
				completed={createCompleted}
				error={createError || undefined}
				onviewserver={viewServer}
			/>
		{/if}
	</div>
</div>

<style>
	.wizard {
		display: flex;
		justify-content: center;
		padding: 2rem;
	}

	.wizard-container {
		width: 100%;
		max-width: 640px;
	}
</style>
```

- [ ] **Step 3: Verify the frontend compiles**

Run: `cd apps/web && npx svelte-check --tsconfig ./tsconfig.json`
Expected: No errors (warnings are OK)

- [ ] **Step 4: Fix any type/import issues found by svelte-check**

Address any missing imports, type mismatches, or API function signatures that don't match. The wizard shell calls `api.createServer`, `api.cloneServer`, `api.downloadProfile`, `api.copyProfile` — verify these exist in client.ts with the right signatures. If any are missing, add them.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/routes/\(app\)/servers/new/+page.svelte
git commit -m "feat: rewrite server creation wizard with two-tier category/implementation flow

Replaces the monolithic 1800-line wizard with a component-based architecture:
- Step 1: Choose category (Vanilla/Plugins/Mods/Bedrock/Template)
- Step 2: Choose implementation (Paper/Spigot/etc or Forge/NeoForge/Fabric/Quilt)
- Step 3: Version selection via dedicated picker components
- Step 4: Name and create
- Removes CurseForge as a server creation path"
```

---

## Phase 6: Final Integration & Verification

### Task 17: End-to-End Verification

- [ ] **Step 1: Verify full backend builds**

Run: `dotnet build apps/MineOS.Api/MineOS.Api.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Verify frontend builds**

Run: `cd apps/web && npm run build`
Expected: Build succeeds

- [ ] **Step 3: Verify svelte-check passes**

Run: `cd apps/web && npx svelte-check --tsconfig ./tsconfig.json`
Expected: No errors

- [ ] **Step 4: Run existing tests**

Run: `dotnet test`
Expected: All existing tests pass (new services have no tests yet — they follow the existing pattern of no unit tests for HTTP client services)

- [ ] **Step 5: Final commit with any fixes**

```bash
git add -A
git commit -m "fix: address integration issues from server creation revamp"
```
