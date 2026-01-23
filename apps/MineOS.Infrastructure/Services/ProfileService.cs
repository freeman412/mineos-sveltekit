using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class ProfileService : IProfileService
{
    private const string BuildToolsJarName = "BuildTools.jar";
    private const string BuildToolsUrl =
        "https://hub.spigotmc.org/jenkins/job/BuildTools/lastSuccessfulBuild/artifact/target/BuildTools.jar";
    private const string RestartFlagFile = ".mineos-restart-required";
    private const string PaperProjectUrl = "https://api.papermc.io/v2/projects/paper";
    private const string MojangVersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const int PaperVersionLimit = 20;
    private static readonly TimeSpan PaperCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim PaperCacheLock = new(1, 1);
    private static DateTimeOffset? _paperLastFetch;
    private static List<ProfileDto> _paperCache = new();
    private static readonly TimeSpan VanillaCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim VanillaCacheLock = new(1, 1);
    private static DateTimeOffset? _vanillaLastFetch;
    private static List<ProfileDto> _vanillaCache = new();
    private static readonly ConcurrentDictionary<string, BuildToolsRunState> BuildToolsRuns = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ILogger<ProfileService> _logger;
    private readonly HostOptions _hostOptions;
    private readonly HttpClient _httpClient;
    private readonly IBackgroundJobService _jobService;

    public ProfileService(
        ILogger<ProfileService> logger,
        IOptions<HostOptions> hostOptions,
        HttpClient httpClient,
        IBackgroundJobService jobService)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
        _httpClient = httpClient;
        _jobService = jobService;
    }

    private string GetProfilesPath() =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ProfilesPathSegment);

    private string GetProfilesFilePath() =>
        Path.Combine(GetProfilesPath(), "profiles.json");

    private string GetBuildToolsLogsPath() =>
        Path.Combine(_hostOptions.BaseDirectory, "logs", "buildtools");

    private string GetBuildToolsLogPath(string runId) =>
        Path.Combine(GetBuildToolsLogsPath(), $"{runId}.log");

    private string GetBuildToolsWorkPath(string profileId) =>
        Path.Combine(Path.GetTempPath(), "mineos-buildtools", profileId);

    private string GetProfilePath(string id) =>
        Path.Combine(GetProfilesPath(), id);

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetProfileJarPath(ProfileDto profile)
    {
        var filename = string.IsNullOrWhiteSpace(profile.Filename) ? $"{profile.Id}.jar" : profile.Filename;
        return Path.Combine(GetProfilePath(profile.Id), filename);
    }

    public async Task<IReadOnlyList<ProfileDto>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await LoadProfilesAsync(cancellationToken);
        var vanillaProfiles = await GetVanillaProfilesAsync(cancellationToken);
        var paperProfiles = await GetPaperProfilesAsync(cancellationToken);
        var buildToolsProfiles = await DiscoverBuildToolsProfilesAsync(cancellationToken);
        var combined = new Dictionary<string, ProfileDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            combined[profile.Id] = profile;
        }

        foreach (var profile in vanillaProfiles)
        {
            combined[profile.Id] = profile;
        }

        foreach (var profile in paperProfiles)
        {
            combined[profile.Id] = profile;
        }

        foreach (var profile in buildToolsProfiles)
        {
            if (combined.TryGetValue(profile.Id, out var existing))
            {
                if (string.Equals(existing.Type, "buildtools", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(existing.Group))
                {
                    combined[profile.Id] = profile;
                }

                continue;
            }

            combined[profile.Id] = profile;
        }

        var ordered = combined.Values
            .OrderBy(p => p.Group)
            .ThenByDescending(p => TryParseVersion(p.Version) ?? new Version(0, 0))
            .ThenBy(p => p.Id)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var profile = ordered[i];
            var jarPath = GetProfileJarPath(profile);
            var filename = Path.GetFileName(jarPath);
            ordered[i] = profile with
            {
                Filename = filename,
                Downloaded = File.Exists(jarPath)
            };
        }

        return ordered;
    }

    public async Task<ProfileDto?> GetProfileAsync(string id, CancellationToken cancellationToken)
    {
        var profiles = await ListProfilesAsync(cancellationToken);
        return profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> DownloadProfileAsync(string id, CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(id, cancellationToken);
        if (profile == null)
        {
            throw new ArgumentException($"Profile '{id}' not found");
        }

        if (string.IsNullOrWhiteSpace(profile.Url))
        {
            throw new InvalidOperationException($"Profile '{id}' does not have a download URL");
        }

        var profilePath = GetProfilePath(profile.Id);
        Directory.CreateDirectory(profilePath);

        var jarPath = GetProfileJarPath(profile);

        using var response = await _httpClient.GetAsync(profile.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var fileStream = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation("Downloaded profile {ProfileId} to {JarPath}", id, jarPath);

        return jarPath;
    }

    public async Task CopyProfileToServerAsync(string profileId, string serverName, CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(profileId, cancellationToken);
        if (profile == null)
        {
            throw new ArgumentException($"Profile '{profileId}' not found");
        }

        var profileJarPath = GetProfileJarPath(profile);
        if (!File.Exists(profileJarPath))
        {
            throw new FileNotFoundException($"Profile JAR not downloaded: {profileId}");
        }

        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var jarFilename = Path.GetFileName(profileJarPath);
        var targetJarPath = Path.Combine(serverPath, jarFilename);

        File.Copy(profileJarPath, targetJarPath, overwrite: true);
        await OwnershipHelper.ChangeOwnershipAsync(
            targetJarPath,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);
        await UpdateServerConfigJarAsync(serverPath, jarFilename, cancellationToken);
        await MarkRestartRequiredAsync(serverPath, cancellationToken);

        _logger.LogInformation("Copied profile {ProfileId} to server {ServerName}", profileId, serverName);
    }

    public async IAsyncEnumerable<ProfileDownloadProgressDto> StreamDownloadProgressAsync(
        string id,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(id, cancellationToken);
        if (profile == null || string.IsNullOrWhiteSpace(profile.Url))
        {
            yield break;
        }

        var profilePath = GetProfilePath(id);
        Directory.CreateDirectory(profilePath);

        var jarPath = GetProfileJarPath(profile);

        yield return new ProfileDownloadProgressDto(0, null, 0, "Starting download");

        using var response = await _httpClient.GetAsync(profile.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        long bytesDownloaded = 0;

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesDownloaded += bytesRead;

            var percentage = totalBytes.HasValue ? (int)((bytesDownloaded * 100) / totalBytes.Value) : 0;

            yield return new ProfileDownloadProgressDto(
                bytesDownloaded,
                totalBytes,
                percentage,
                "Downloading"
            );
        }

        yield return new ProfileDownloadProgressDto(bytesDownloaded, totalBytes, 100, "Complete");

        _logger.LogInformation("Downloaded profile {ProfileId} to {JarPath}", id, jarPath);
    }

    public async Task<BuildToolsRunDto> StartBuildToolsAsync(string group, string version, CancellationToken cancellationToken)
    {
        var request = NormalizeBuildToolsRequest(group, version);
        var runId = Guid.NewGuid().ToString("N");
        var logPath = GetBuildToolsLogPath(runId);
        var startedAt = DateTimeOffset.UtcNow;

        Directory.CreateDirectory(GetBuildToolsLogsPath());
        await File.WriteAllTextAsync(
            logPath,
            $"[{startedAt:O}] BuildTools run started for {request.Group} {request.Version}{Environment.NewLine}",
            cancellationToken);

        var run = new BuildToolsRunState(runId, request.ProfileId, request.Group, request.Version, logPath, startedAt);
        BuildToolsRuns[runId] = run;

        string? jobId = null;
        jobId = _jobService.QueueJob(
            "buildtools",
            request.ProfileId,
            async (services, progress, token) =>
            {
                var resolvedJobId = jobId ?? string.Empty;
                progress.Report(new JobProgressDto(
                    resolvedJobId,
                    "buildtools",
                    request.ProfileId,
                    "running",
                    5,
                    $"Preparing BuildTools {request.Group} {request.Version}",
                    DateTimeOffset.UtcNow));

                try
                {
                    await BuildToolsInternalAsync(request, run, progress, resolvedJobId, token);
                    run.MarkCompleted();
                    await AppendLogLineAsync(logPath, "BuildTools completed successfully.");
                    progress.Report(new JobProgressDto(
                        resolvedJobId,
                        "buildtools",
                        request.ProfileId,
                        "running",
                        100,
                        "BuildTools completed",
                        DateTimeOffset.UtcNow));
                }
                catch (Exception ex)
                {
                    run.MarkFailed(ex.Message);
                    await AppendLogLineAsync(logPath, $"BuildTools failed: {ex.Message}");
                    progress.Report(new JobProgressDto(
                        resolvedJobId,
                        "buildtools",
                        request.ProfileId,
                        "failed",
                        100,
                        ex.Message,
                        DateTimeOffset.UtcNow));
                    _logger.LogError(ex, "BuildTools run {RunId} failed", runId);
                    throw;
                }
            });

        return run.ToDto();
    }

    public Task<BuildToolsRunDto?> GetBuildToolsRunAsync(string runId, CancellationToken cancellationToken)
    {
        if (BuildToolsRuns.TryGetValue(runId, out var run))
        {
            return Task.FromResult<BuildToolsRunDto?>(run.ToDto());
        }

        return Task.FromResult<BuildToolsRunDto?>(null);
    }

    public Task<IReadOnlyList<BuildToolsRunDto>> ListBuildToolsRunsAsync(CancellationToken cancellationToken)
    {
        var runs = BuildToolsRuns.Values
            .Select(run => run.ToDto())
            .OrderByDescending(run => run.StartedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<BuildToolsRunDto>>(runs);
    }

    public async IAsyncEnumerable<BuildToolsLogEntryDto> StreamBuildToolsLogAsync(
        string runId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!BuildToolsRuns.TryGetValue(runId, out var run))
        {
            throw new ArgumentException($"BuildTools run '{runId}' not found");
        }

        var logPath = run.LogPath;
        while (!File.Exists(logPath) && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(200, cancellationToken);
        }

        await using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (line != null)
            {
                var status = run.ToDto().Status;
                yield return new BuildToolsLogEntryDto(DateTimeOffset.UtcNow, line, status);
                continue;
            }

            var snapshot = run.ToDto();
            if (snapshot.Status is "completed" or "failed")
            {
                try
                {
                    await Task.Delay(200, cancellationToken);
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                if (line != null)
                {
                    yield return new BuildToolsLogEntryDto(DateTimeOffset.UtcNow, line, snapshot.Status);
                }

                yield break;
            }

            try
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    private BuildToolsRequest NormalizeBuildToolsRequest(string group, string version)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            throw new ArgumentException("BuildTools group is required");
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("BuildTools version is required");
        }

        var normalizedGroup = group.Trim().ToLowerInvariant();
        var normalizedVersion = version.Trim();

        var compileArg = normalizedGroup switch
        {
            "spigot" => "SPIGOT",
            "craftbukkit" => "CRAFTBUKKIT",
            "bukkit" => "CRAFTBUKKIT",
            _ => throw new ArgumentException($"Unsupported BuildTools group: {group}")
        };

        var profileId = $"{normalizedGroup}-{normalizedVersion}";
        return new BuildToolsRequest(normalizedGroup, normalizedVersion, compileArg, profileId);
    }

    private async Task BuildToolsInternalAsync(
        BuildToolsRequest request,
        BuildToolsRunState run,
        IProgress<JobProgressDto> progress,
        string jobId,
        CancellationToken cancellationToken)
    {
        var profileId = request.ProfileId;
        var profilePath = GetProfilePath(profileId);
        Directory.CreateDirectory(profilePath);
        var workPath = GetBuildToolsWorkPath(profileId);
        Directory.CreateDirectory(workPath);

        await using var logStream = new FileStream(run.LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var logWriter = new StreamWriter(logStream) { AutoFlush = true };
        var logLock = new SemaphoreSlim(1, 1);

        async Task WriteLogAsync(string message)
        {
            await logLock.WaitAsync(cancellationToken);
            try
            {
                await logWriter.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] {message}");
            }
            finally
            {
                logLock.Release();
            }
        }

        await WriteLogAsync($"Using BuildTools workspace: {workPath}");

        var buildToolsPath = Path.Combine(workPath, BuildToolsJarName);
        if (!File.Exists(buildToolsPath))
        {
            progress.Report(new JobProgressDto(
                jobId,
                "buildtools",
                request.ProfileId,
                "running",
                15,
                "Downloading BuildTools.jar",
                DateTimeOffset.UtcNow));
            await WriteLogAsync("Downloading BuildTools.jar");
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, BuildToolsUrl);
            requestMessage.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MineOS/1.0");
            requestMessage.Headers.Accept.ParseAdd("application/java-archive");
            requestMessage.Headers.Accept.ParseAdd("application/octet-stream");
            requestMessage.Headers.Referrer = new Uri("https://hub.spigotmc.org/jenkins/job/BuildTools/lastSuccessfulBuild/");

            using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var fileStream = new FileStream(buildToolsPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fileStream, cancellationToken);
        }

        var lockPaths = new[]
        {
            Path.Combine(workPath, "BuildData", ".git", "index.lock"),
            Path.Combine(workPath, "Bukkit", ".git", "index.lock"),
            Path.Combine(workPath, "CraftBukkit", ".git", "index.lock"),
            Path.Combine(workPath, "Spigot", ".git", "index.lock")
        };
        foreach (var lockPath in lockPaths)
        {
            if (!File.Exists(lockPath))
            {
                continue;
            }

            try
            {
                File.Delete(lockPath);
                await WriteLogAsync($"Removed stale git lock file: {lockPath}");
            }
            catch (Exception ex)
            {
                await WriteLogAsync($"Failed to remove git lock file {lockPath}: {ex.Message}");
            }
        }

        progress.Report(new JobProgressDto(
            jobId,
            "buildtools",
            request.ProfileId,
            "running",
            35,
            "Running BuildTools",
            DateTimeOffset.UtcNow));

        var args = $"-jar {BuildToolsJarName} --rev {request.Version} --compile {request.CompileArg}";
        await WriteLogAsync($"Running: java {args}");

        var psi = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = args,
            WorkingDirectory = workPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start BuildTools process");
        }

        Task ReadStreamAsync(StreamReader reader, string prefix)
        {
            return Task.Run(async () =>
            {
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    await WriteLogAsync($"{prefix}{line}");
                }
            }, cancellationToken);
        }

        var stdoutTask = ReadStreamAsync(process.StandardOutput, string.Empty);
        var stderrTask = ReadStreamAsync(process.StandardError, "ERR ");

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask);

        await WriteLogAsync($"BuildTools exited with code {process.ExitCode}");

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("BuildTools failed. Check the log output for details.");
        }

        var sourceJarName = request.Group == "spigot"
            ? $"spigot-{request.Version}.jar"
            : $"craftbukkit-{request.Version}.jar";
        var sourceJarPath = Path.Combine(workPath, sourceJarName);

        if (!File.Exists(sourceJarPath))
        {
            var candidate = Directory.GetFiles(workPath, $"*{request.Version}*.jar")
                .FirstOrDefault(path => !path.EndsWith(BuildToolsJarName, StringComparison.OrdinalIgnoreCase));
            if (candidate == null)
            {
                throw new FileNotFoundException($"BuildTools output not found for {request.Group} {request.Version}");
            }

            sourceJarPath = candidate;
        }

        var targetJarName = $"{profileId}.jar";
        var targetJarPath = Path.Combine(profilePath, targetJarName);
        File.Copy(sourceJarPath, targetJarPath, overwrite: true);

        progress.Report(new JobProgressDto(
            jobId,
            "buildtools",
            request.ProfileId,
            "running",
            85,
            "Saving profile",
            DateTimeOffset.UtcNow));

        var profile = new ProfileDto(
            profileId,
            request.Group,
            "buildtools",
            request.Version,
            DateTimeOffset.UtcNow.ToString("O"),
            BuildToolsUrl,
            targetJarName,
            true,
            null);

        var profiles = await LoadProfilesAsync(cancellationToken);
        var index = profiles.FindIndex(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        await SaveProfilesAsync(profiles, cancellationToken);
        progress.Report(new JobProgressDto(
            jobId,
            "buildtools",
            request.ProfileId,
            "running",
            95,
            "Profile saved",
            DateTimeOffset.UtcNow));

        _logger.LogInformation("Built BuildTools profile {ProfileId}", profileId);
    }

    public async Task DeleteBuildToolsAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Profile id is required");
        }

        var profiles = await LoadProfilesAsync(cancellationToken);
        var updated = profiles.Where(p => !p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        await SaveProfilesAsync(updated, cancellationToken);

        var profilePath = GetProfilePath(id);
        if (Directory.Exists(profilePath))
        {
            Directory.Delete(profilePath, recursive: true);
        }

        _logger.LogInformation("Deleted BuildTools profile {ProfileId}", id);
    }

    private async Task UpdateServerConfigJarAsync(string serverPath, string jarFilename, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(serverPath, "server.config");
        if (!File.Exists(configPath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken);
        var sections = IniParser.ParseWithSections(content);

        if (!sections.TryGetValue("java", out var javaSection))
        {
            javaSection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            sections["java"] = javaSection;
        }

        javaSection["jarfile"] = jarFilename;

        var updated = IniParser.WriteWithSections(sections);
        await File.WriteAllTextAsync(configPath, updated, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(
            configPath,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);
    }

    private async Task<List<ProfileDto>> LoadProfilesAsync(CancellationToken cancellationToken)
    {
        var profilesFile = GetProfilesFilePath();
        if (!File.Exists(profilesFile))
        {
            return GetDefaultProfiles().ToList();
        }

        var json = await File.ReadAllTextAsync(profilesFile, cancellationToken);
        var profiles = JsonSerializer.Deserialize<List<ProfileDto>>(json, JsonOptions) ?? new List<ProfileDto>();
        return profiles;
    }

    private async Task<IReadOnlyList<ProfileDto>> GetVanillaProfilesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_vanillaLastFetch.HasValue &&
            now - _vanillaLastFetch.Value < VanillaCacheTtl &&
            _vanillaCache.Count > 0)
        {
            return _vanillaCache;
        }

        await VanillaCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_vanillaLastFetch.HasValue &&
                now - _vanillaLastFetch.Value < VanillaCacheTtl &&
                _vanillaCache.Count > 0)
            {
                return _vanillaCache;
            }

            var fetched = await FetchVanillaProfilesAsync(cancellationToken);
            if (fetched.Count > 0)
            {
                _vanillaCache = fetched.ToList();
                _vanillaLastFetch = DateTimeOffset.UtcNow;
            }
            else if (_vanillaCache.Count == 0)
            {
                _vanillaLastFetch = DateTimeOffset.UtcNow;
            }

            return _vanillaCache;
        }
        finally
        {
            VanillaCacheLock.Release();
        }
    }

    private async Task<IReadOnlyList<ProfileDto>> FetchVanillaProfilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(MojangVersionManifestUrl, cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("versions", out var versionsElement) ||
                versionsElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ProfileDto>();
            }

            var versions = new List<MojangVersionInfo>();
            foreach (var element in versionsElement.EnumerateArray())
            {
                var type = element.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (!string.Equals(type, "release", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = element.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                var url = element.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                var releaseTime = element.TryGetProperty("releaseTime", out var rtElement)
                    ? rtElement.GetString()
                    : element.TryGetProperty("time", out var timeElement) ? timeElement.GetString() : null;

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var parsedTime = TryParseReleaseTime(releaseTime);
                versions.Add(new MojangVersionInfo(
                    id,
                    url,
                    releaseTime ?? DateTimeOffset.UtcNow.ToString("O"),
                    parsedTime));
            }

            var ordered = versions
                .OrderByDescending(v => v.ReleaseTimeParsed ?? DateTimeOffset.MinValue)
                .ToList();

            var results = new List<ProfileDto>();
            foreach (var version in ordered)
            {
                try
                {
                    var versionJson = await _httpClient.GetStringAsync(version.Url, cancellationToken);
                    using var versionDoc = JsonDocument.Parse(versionJson);

                    if (!versionDoc.RootElement.TryGetProperty("downloads", out var downloadsElement) ||
                        downloadsElement.ValueKind != JsonValueKind.Object ||
                        !downloadsElement.TryGetProperty("server", out var serverElement))
                    {
                        continue;
                    }

                    var serverUrl = serverElement.TryGetProperty("url", out var urlElement)
                        ? urlElement.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(serverUrl))
                    {
                        continue;
                    }

                    var filename = $"vanilla-{version.Id}.jar";
                    results.Add(new ProfileDto(
                        $"vanilla-{version.Id}",
                        "vanilla",
                        "release",
                        version.Id,
                        version.ReleaseTime,
                        serverUrl,
                        filename,
                        false,
                        null));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load vanilla version {Version}", version.Id);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch vanilla profiles");
            return Array.Empty<ProfileDto>();
        }
    }

    private async Task<IReadOnlyList<ProfileDto>> GetPaperProfilesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_paperLastFetch.HasValue &&
            now - _paperLastFetch.Value < PaperCacheTtl &&
            _paperCache.Count > 0)
        {
            return _paperCache;
        }

        await PaperCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_paperLastFetch.HasValue &&
                now - _paperLastFetch.Value < PaperCacheTtl &&
                _paperCache.Count > 0)
            {
                return _paperCache;
            }

            var fetched = await FetchPaperProfilesAsync(cancellationToken);
            if (fetched.Count > 0)
            {
                _paperCache = fetched.ToList();
                _paperLastFetch = DateTimeOffset.UtcNow;
            }
            else if (_paperCache.Count == 0)
            {
                _paperLastFetch = DateTimeOffset.UtcNow;
            }

            return _paperCache;
        }
        finally
        {
            PaperCacheLock.Release();
        }
    }

    private async Task<IReadOnlyList<ProfileDto>> FetchPaperProfilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(PaperProjectUrl, cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("versions", out var versionsElement) ||
                versionsElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ProfileDto>();
            }

            var versions = new List<(string Raw, Version Parsed)>();
            foreach (var element in versionsElement.EnumerateArray())
            {
                var versionText = element.GetString();
                if (string.IsNullOrWhiteSpace(versionText))
                {
                    continue;
                }

                if (!IsStablePaperVersion(versionText))
                {
                    continue;
                }

                if (Version.TryParse(versionText, out var parsed))
                {
                    versions.Add((versionText, parsed));
                }
            }

            var recentVersions = versions
                .OrderByDescending(v => v.Parsed)
                .Take(PaperVersionLimit)
                .Select(v => v.Raw)
                .ToList();

            var results = new List<ProfileDto>();
            foreach (var version in recentVersions)
            {
                try
                {
                    var build = await GetLatestPaperBuildAsync(version, cancellationToken);
                    if (build == null)
                    {
                        continue;
                    }

                    var url =
                        $"https://api.papermc.io/v2/projects/paper/versions/{version}/builds/{build.Build}/downloads/{build.FileName}";

                    results.Add(new ProfileDto(
                        $"paper-{version}",
                        "paper",
                        "release",
                        version,
                        build.Time,
                        url,
                        build.FileName,
                        false,
                        null));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Paper build for {Version}", version);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Paper profiles");
            return Array.Empty<ProfileDto>();
        }
    }

    private async Task<PaperBuildInfo?> GetLatestPaperBuildAsync(string version, CancellationToken cancellationToken)
    {
        var versionUrl = $"{PaperProjectUrl}/versions/{version}";
        var json = await _httpClient.GetStringAsync(versionUrl, cancellationToken);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("builds", out var buildsElement) ||
            buildsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builds = buildsElement
            .EnumerateArray()
            .Select(element => element.GetInt32())
            .ToList();

        if (builds.Count == 0)
        {
            return null;
        }

        var latestBuild = builds[^1];
        var buildUrl = $"{versionUrl}/builds/{latestBuild}";
        var buildJson = await _httpClient.GetStringAsync(buildUrl, cancellationToken);
        using var buildDoc = JsonDocument.Parse(buildJson);

        var time = buildDoc.RootElement.TryGetProperty("time", out var timeElement)
            ? timeElement.GetString() ?? DateTimeOffset.UtcNow.ToString("O")
            : DateTimeOffset.UtcNow.ToString("O");

        string fileName = $"paper-{version}-{latestBuild}.jar";
        if (buildDoc.RootElement.TryGetProperty("downloads", out var downloadsElement) &&
            downloadsElement.TryGetProperty("application", out var appElement) &&
            appElement.TryGetProperty("name", out var nameElement))
        {
            fileName = nameElement.GetString() ?? fileName;
        }

        return new PaperBuildInfo(latestBuild, time, fileName);
    }

    private static bool IsStablePaperVersion(string version)
    {
        return !version.Contains('-', StringComparison.OrdinalIgnoreCase);
    }

    private static Version? TryParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return Version.TryParse(version, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? TryParseReleaseTime(string? releaseTime)
    {
        if (string.IsNullOrWhiteSpace(releaseTime))
        {
            return null;
        }

        return DateTimeOffset.TryParse(releaseTime, out var parsed) ? parsed : null;
    }

    private async Task SaveProfilesAsync(List<ProfileDto> profiles, CancellationToken cancellationToken)
    {
        var profilesPath = GetProfilesPath();
        Directory.CreateDirectory(profilesPath);

        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        await File.WriteAllTextAsync(GetProfilesFilePath(), json, cancellationToken);
    }

    private Task<IReadOnlyList<ProfileDto>> DiscoverBuildToolsProfilesAsync(CancellationToken cancellationToken)
    {
        var results = new List<ProfileDto>();
        var profilesPath = GetProfilesPath();
        if (!Directory.Exists(profilesPath))
        {
            return Task.FromResult<IReadOnlyList<ProfileDto>>(results);
        }

        foreach (var dir in Directory.EnumerateDirectories(profilesPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var group = id.StartsWith("spigot-", StringComparison.OrdinalIgnoreCase)
                ? "spigot"
                : id.StartsWith("craftbukkit-", StringComparison.OrdinalIgnoreCase)
                    ? "craftbukkit"
                    : null;
            if (group == null)
            {
                continue;
            }

            var version = id.Substring(group.Length + 1);
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            var jarPath = Path.Combine(dir, $"{id}.jar");
            if (!File.Exists(jarPath))
            {
                continue;
            }

            var modified = File.GetLastWriteTimeUtc(jarPath);
            results.Add(new ProfileDto(
                id,
                group,
                "buildtools",
                version,
                new DateTimeOffset(modified, TimeSpan.Zero).ToString("O"),
                BuildToolsUrl,
                Path.GetFileName(jarPath),
                true,
                null));
        }

        return Task.FromResult<IReadOnlyList<ProfileDto>>(results);
    }

    private static async Task AppendLogLineAsync(string logPath, string message)
    {
        await using var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        await writer.WriteLineAsync($"[{DateTimeOffset.UtcNow:O}] {message}");
    }

    private static IEnumerable<ProfileDto> GetDefaultProfiles()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return new[]
        {
            new ProfileDto(
                "vanilla-1.20.4",
                "vanilla",
                "release",
                "1.20.4",
                now,
                "https://piston-data.mojang.com/v1/objects/8dd1a28015f51b1803213892b50b7b4fc76e594d/server.jar",
                "vanilla-1.20.4.jar",
                false,
                null
            ),
            new ProfileDto(
                "vanilla-1.19.4",
                "vanilla",
                "release",
                "1.19.4",
                now,
                "https://piston-data.mojang.com/v1/objects/8f3112a1049751cc472ec13e397eade5336ca7ae/server.jar",
                "vanilla-1.19.4.jar",
                false,
                null
            ),
            new ProfileDto(
                "paper-1.20.4",
                "paper",
                "release",
                "1.20.4",
                now,
                "https://api.papermc.io/v2/projects/paper/versions/1.20.4/builds/496/downloads/paper-1.20.4-496.jar",
                "paper-1.20.4.jar",
                false,
                null
            )
        };
    }

    private sealed record BuildToolsRequest(string Group, string Version, string CompileArg, string ProfileId);

    private sealed class BuildToolsRunState
    {
        private readonly object _sync = new();

        public BuildToolsRunState(
            string runId,
            string profileId,
            string group,
            string version,
            string logPath,
            DateTimeOffset startedAt)
        {
            RunId = runId;
            ProfileId = profileId;
            Group = group;
            Version = version;
            LogPath = logPath;
            StartedAt = startedAt;
            Status = "running";
        }

        public string RunId { get; }
        public string ProfileId { get; }
        public string Group { get; }
        public string Version { get; }
        public string LogPath { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public string Status { get; private set; }
        public string? Error { get; private set; }

        public void MarkCompleted()
        {
            lock (_sync)
            {
                Status = "completed";
                CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        public void MarkFailed(string error)
        {
            lock (_sync)
            {
                Status = "failed";
                Error = error;
                CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        public BuildToolsRunDto ToDto()
        {
            lock (_sync)
            {
                return new BuildToolsRunDto(
                    RunId,
                    ProfileId,
                    Group,
                    Version,
                    Status,
                    StartedAt,
                    CompletedAt,
                    Error);
            }
        }
    }

    private record MojangVersionInfo(string Id, string Url, string ReleaseTime, DateTimeOffset? ReleaseTimeParsed);
    private record PaperBuildInfo(int Build, string Time, string FileName);

    private async Task MarkRestartRequiredAsync(string serverPath, CancellationToken cancellationToken)
    {
        try
        {
            var flagPath = Path.Combine(serverPath, RestartFlagFile);
            await File.WriteAllTextAsync(flagPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
            await OwnershipHelper.ChangeOwnershipAsync(
                flagPath,
                _hostOptions.RunAsUid,
                _hostOptions.RunAsGid,
                _logger,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark restart required for {ServerPath}", serverPath);
        }
    }
}
