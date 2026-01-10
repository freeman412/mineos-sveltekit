using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class ProfileService : IProfileService
{
    private readonly ILogger<ProfileService> _logger;
    private readonly HostOptions _hostOptions;
    private readonly HttpClient _httpClient;

    public ProfileService(
        ILogger<ProfileService> logger,
        IOptions<HostOptions> hostOptions,
        HttpClient httpClient)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
        _httpClient = httpClient;
    }

    private string GetProfilesPath() =>
        Path.Combine(_hostOptions.BaseDirectory, "profiles");

    private string GetProfilePath(string id) =>
        Path.Combine(GetProfilesPath(), id);

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    public async Task<IEnumerable<ProfileInfoDto>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var profilesPath = GetProfilesPath();
        var profilesFile = Path.Combine(profilesPath, "profiles.json");

        if (!File.Exists(profilesFile))
        {
            // Return default profiles if no custom profiles exist
            return GetDefaultProfiles();
        }

        var json = await File.ReadAllTextAsync(profilesFile, cancellationToken);
        var profiles = JsonSerializer.Deserialize<List<ProfileInfoDto>>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? new List<ProfileInfoDto>();

        // Check which profiles are downloaded
        return profiles.Select(p => p with
        {
            Downloaded = File.Exists(Path.Combine(GetProfilePath(p.Id), $"{p.Id}.jar")),
            Size = GetProfileSize(p.Id)
        });
    }

    public async Task<ProfileInfoDto?> GetProfileAsync(string id, CancellationToken cancellationToken)
    {
        var profiles = await ListProfilesAsync(cancellationToken);
        return profiles.FirstOrDefault(p => p.Id == id);
    }

    public async Task<string> DownloadProfileAsync(string id, CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(id, cancellationToken);
        if (profile == null)
        {
            throw new ArgumentException($"Profile '{id}' not found");
        }

        var profilePath = GetProfilePath(id);
        Directory.CreateDirectory(profilePath);

        var jarPath = Path.Combine(profilePath, $"{id}.jar");

        // Download the JAR file
        using var response = await _httpClient.GetAsync(profile.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var fileStream = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None);
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

        var profileJarPath = Path.Combine(GetProfilePath(profileId), $"{profileId}.jar");
        if (!File.Exists(profileJarPath))
        {
            throw new FileNotFoundException($"Profile JAR not downloaded: {profileId}");
        }

        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var targetJarPath = Path.Combine(serverPath, $"{profileId}.jar");

        // Copy the JAR file
        File.Copy(profileJarPath, targetJarPath, overwrite: true);

        _logger.LogInformation("Copied profile {ProfileId} to server {ServerName}", profileId, serverName);
    }

    public async IAsyncEnumerable<ProfileDownloadProgressDto> StreamDownloadProgressAsync(
        string id,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(id, cancellationToken);
        if (profile == null)
        {
            yield break;
        }

        var profilePath = GetProfilePath(id);
        Directory.CreateDirectory(profilePath);

        var jarPath = Path.Combine(profilePath, $"{id}.jar");

        yield return new ProfileDownloadProgressDto(0, null, 0, "Starting download");

        using var response = await _httpClient.GetAsync(profile.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None);

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

    private long? GetProfileSize(string id)
    {
        var jarPath = Path.Combine(GetProfilePath(id), $"{id}.jar");
        if (!File.Exists(jarPath))
        {
            return null;
        }

        return new FileInfo(jarPath).Length;
    }

    private static IEnumerable<ProfileInfoDto> GetDefaultProfiles()
    {
        return new[]
        {
            new ProfileInfoDto(
                "vanilla-1.20.4",
                "Vanilla 1.20.4",
                "1.20.4",
                "vanilla",
                "https://piston-data.mojang.com/v1/objects/8dd1a28015f51b1803213892b50b7b4fc76e594d/server.jar",
                false,
                null
            ),
            new ProfileInfoDto(
                "vanilla-1.19.4",
                "Vanilla 1.19.4",
                "1.19.4",
                "vanilla",
                "https://piston-data.mojang.com/v1/objects/8f3112a1049751cc472ec13e397eade5336ca7ae/server.jar",
                false,
                null
            ),
            new ProfileInfoDto(
                "paper-1.20.4",
                "Paper 1.20.4",
                "1.20.4",
                "paper",
                "https://api.papermc.io/v2/projects/paper/versions/1.20.4/builds/496/downloads/paper-1.20.4-496.jar",
                false,
                null
            )
        };
    }
}
