using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public sealed class ModrinthService : IModrinthService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<ModrinthService> _logger;

    public ModrinthService(HttpClient httpClient, ILogger<ModrinthService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<ModrinthSearchResultDto> SearchModsAsync(
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        return SearchAsync("mod", query, index, pageSize, loader, gameVersion, cancellationToken);
    }

    public Task<ModrinthSearchResultDto> SearchModpacksAsync(
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        return SearchAsync("modpack", query, index, pageSize, loader, gameVersion, cancellationToken);
    }

    public async Task<ModrinthSearchResultDto> SearchPluginsAsync(
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        return await SearchAsync("plugin", query, index, pageSize, loader, gameVersion, cancellationToken);
    }

    public async Task<ModrinthSearchResultDto> SearchResourcePacksAsync(
        string query,
        int index,
        int pageSize,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        // Resource packs don't use loaders, so pass null for loader parameter
        return await SearchAsync("resourcepack", query, index, pageSize, null, gameVersion, cancellationToken);
    }

    public async Task<ModrinthProjectDto?> GetProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<ModrinthProjectResponse>(
            $"project/{Uri.EscapeDataString(projectId)}",
            JsonOptions,
            cancellationToken);

        if (response == null)
        {
            return null;
        }

        return new ModrinthProjectDto(
            response.Id,
            response.Slug,
            response.Title,
            response.Description,
            response.Body,
            response.ProjectType,
            response.Downloads,
            response.Categories?.ToArray() ?? Array.Empty<string>(),
            response.GameVersions?.ToArray() ?? Array.Empty<string>(),
            response.Loaders?.ToArray() ?? Array.Empty<string>(),
            response.IconUrl,
            response.ClientSide,
            response.ServerSide);
    }

    public async Task<IReadOnlyList<ModrinthVersionDto>> GetProjectVersionsAsync(
        string projectId,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(loader))
        {
            var loaderJson = JsonSerializer.Serialize(new[] { loader });
            queryParams.Add($"loaders={Uri.EscapeDataString(loaderJson)}");
        }

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            var versionJson = JsonSerializer.Serialize(new[] { gameVersion });
            queryParams.Add($"game_versions={Uri.EscapeDataString(versionJson)}");
        }

        var url = $"project/{Uri.EscapeDataString(projectId)}/version";
        if (queryParams.Count > 0)
        {
            url += "?" + string.Join("&", queryParams);
        }

        var response = await _httpClient.GetFromJsonAsync<List<ModrinthVersionResponse>>(
            url,
            JsonOptions,
            cancellationToken);

        if (response == null)
        {
            return Array.Empty<ModrinthVersionDto>();
        }

        return response.Select(MapVersion).ToList();
    }

    public async Task<ModrinthVersionDto?> GetVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<ModrinthVersionResponse>(
            $"version/{Uri.EscapeDataString(versionId)}",
            JsonOptions,
            cancellationToken);

        return response == null ? null : MapVersion(response);
    }

    public async Task<ModrinthVersionDto?> GetVersionByFileHashAsync(
        string hash,
        string algorithm,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<ModrinthVersionResponse>(
            $"version_file/{Uri.EscapeDataString(hash)}?algorithm={Uri.EscapeDataString(algorithm)}",
            JsonOptions,
            cancellationToken);

        return response == null ? null : MapVersion(response);
    }

    public async Task<Stream> OpenDownloadStreamAsync(string url, CancellationToken cancellationToken)
    {
        return await _httpClient.GetStreamAsync(url, cancellationToken);
    }

    private async Task<ModrinthSearchResultDto> SearchAsync(
        string projectType,
        string query,
        int index,
        int pageSize,
        string? loader,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        var facets = new List<List<string>>
        {
            new() { $"project_type:{projectType}" }
        };

        if (!string.IsNullOrWhiteSpace(loader))
        {
            facets.Add(new List<string> { $"categories:{loader}" });
        }

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            facets.Add(new List<string> { $"versions:{gameVersion}" });
        }

        var facetsJson = JsonSerializer.Serialize(facets);
        var url =
            $"search?query={Uri.EscapeDataString(query)}&offset={index}&limit={pageSize}&facets={Uri.EscapeDataString(facetsJson)}";

        var response = await _httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, JsonOptions, cancellationToken);
        if (response == null)
        {
            return new ModrinthSearchResultDto(index, pageSize, 0, Array.Empty<ModrinthProjectHitDto>());
        }

        var results = response.Hits.Select(hit => new ModrinthProjectHitDto(
            hit.ProjectId,
            hit.Slug,
            hit.Title,
            hit.Description,
            hit.IconUrl,
            hit.Downloads,
            hit.Versions?.ToArray() ?? Array.Empty<string>(),
            hit.Categories?.ToArray() ?? Array.Empty<string>())).ToList();

        return new ModrinthSearchResultDto(response.Offset, response.Limit, response.TotalHits, results);
    }

    private ModrinthVersionDto MapVersion(ModrinthVersionResponse response)
    {
        var files = response.Files?.Select(file => new ModrinthVersionFileDto(
            file.Url,
            file.FileName,
            file.Size,
            file.Primary)).ToList() ?? new List<ModrinthVersionFileDto>();

        var dependencies = response.Dependencies?.Select(dep => new ModrinthDependencyDto(
            dep.ProjectId,
            dep.VersionId,
            dep.DependencyType)).ToList() ?? new List<ModrinthDependencyDto>();

        return new ModrinthVersionDto(
            response.Id,
            response.ProjectId,
            response.Name ?? response.VersionNumber,
            response.VersionNumber,
            response.DatePublished,
            response.Downloads,
            response.GameVersions?.ToArray() ?? Array.Empty<string>(),
            response.Loaders?.ToArray() ?? Array.Empty<string>(),
            files,
            dependencies);
    }

    private sealed record ModrinthSearchResponse(
        [property: JsonPropertyName("hits")] List<ModrinthProjectHitResponse> Hits,
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("limit")] int Limit,
        [property: JsonPropertyName("total_hits")] int TotalHits);

    private sealed record ModrinthProjectHitResponse(
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("downloads")] int Downloads,
        [property: JsonPropertyName("versions")] List<string>? Versions,
        [property: JsonPropertyName("categories")] List<string>? Categories);

    private sealed record ModrinthProjectResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("project_type")] string ProjectType,
        [property: JsonPropertyName("downloads")] int Downloads,
        [property: JsonPropertyName("categories")] List<string>? Categories,
        [property: JsonPropertyName("game_versions")] List<string>? GameVersions,
        [property: JsonPropertyName("loaders")] List<string>? Loaders,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("client_side")] string? ClientSide,
        [property: JsonPropertyName("server_side")] string? ServerSide);

    private sealed record ModrinthVersionResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("version_number")] string VersionNumber,
        [property: JsonPropertyName("date_published")] DateTimeOffset DatePublished,
        [property: JsonPropertyName("downloads")] int Downloads,
        [property: JsonPropertyName("game_versions")] List<string>? GameVersions,
        [property: JsonPropertyName("loaders")] List<string>? Loaders,
        [property: JsonPropertyName("files")] List<ModrinthVersionFileResponse>? Files,
        [property: JsonPropertyName("dependencies")] List<ModrinthDependencyResponse>? Dependencies);

    private sealed record ModrinthVersionFileResponse(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("primary")] bool Primary);

    private sealed record ModrinthDependencyResponse(
        [property: JsonPropertyName("project_id")] string? ProjectId,
        [property: JsonPropertyName("version_id")] string? VersionId,
        [property: JsonPropertyName("dependency_type")] string DependencyType);
}
