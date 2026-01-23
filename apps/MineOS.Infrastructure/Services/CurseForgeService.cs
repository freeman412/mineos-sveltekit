using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.External;
using Microsoft.Extensions.Options;

namespace MineOS.Infrastructure.Services;

public sealed class CurseForgeService : ICurseForgeService
{
    private readonly CurseForgeClient _client;
    private readonly CurseForgeOptions _options;

    public CurseForgeService(CurseForgeClient client, IOptions<CurseForgeOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<CurseForgeSearchResultDto> SearchModsAsync(
        string query,
        int? classId,
        int index,
        int pageSize,
        string? sort,
        string? order,
        long? minDownloads,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query is required");
        }

        var parameters = new List<string>
        {
            $"gameId={_options.GameId}",
            $"index={index}",
            $"pageSize={pageSize}",
            $"searchFilter={Uri.EscapeDataString(query)}"
        };

        if (classId.HasValue)
        {
            parameters.Add($"classId={classId.Value}");
        }

        var sortField = ResolveSortField(sort);
        if (sortField.HasValue)
        {
            var sortOrder = ResolveSortOrder(order, sortField.Value);
            parameters.Add($"sortField={sortField.Value}");
            parameters.Add($"sortOrder={sortOrder}");
        }

        var path = $"/v1/mods/search?{string.Join("&", parameters)}";
        var response = await _client.GetAsync<List<CurseForgeModData>>(path, cancellationToken);

        var results = response.Data
            .Select(MapModSummary)
            .ToList();

        if (minDownloads.HasValue && minDownloads.Value > 0)
        {
            results = results
                .Where(result => result.DownloadCount >= minDownloads.Value)
                .ToList();
        }

        var pagination = response.Pagination ?? new CurseForgePagination
        {
            Index = index,
            PageSize = pageSize,
            ResultCount = results.Count,
            TotalCount = results.Count
        };

        return new CurseForgeSearchResultDto(
            pagination.Index,
            pagination.PageSize,
            results.Count,
            pagination.TotalCount,
            results);
    }

    public async Task<CurseForgeModDto> GetModAsync(int modId, CancellationToken cancellationToken)
    {
        var response = await _client.GetAsync<CurseForgeModData>($"/v1/mods/{modId}", cancellationToken);
        return MapMod(response.Data);
    }

    public async Task<CurseForgeFileDto> GetModFileAsync(int modId, int fileId, CancellationToken cancellationToken)
    {
        var response = await _client.GetAsync<CurseForgeFileData>($"/v1/mods/{modId}/files/{fileId}", cancellationToken);
        return MapFile(response.Data);
    }

    public async Task<IReadOnlyList<CurseForgeFileDto>> GetModFilesListAsync(
        int modId,
        string? gameVersion,
        CancellationToken cancellationToken)
    {
        var path = $"/v1/mods/{modId}/files?pageSize=50";
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            path += $"&gameVersion={Uri.EscapeDataString(gameVersion)}";
        }

        var response = await _client.GetAsync<List<CurseForgeFileData>>(path, cancellationToken);
        return response.Data.Select(MapFile).ToList();
    }

    public async Task<string> GetModFileDownloadUrlAsync(int modId, int fileId, CancellationToken cancellationToken)
    {
        var response = await _client.GetAsync<CurseForgeDownloadUrlData>(
            $"/v1/mods/{modId}/files/{fileId}/download-url",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Data.DownloadUrl))
        {
            throw new InvalidOperationException("CurseForge did not provide a download URL");
        }

        return response.Data.DownloadUrl;
    }

    public async Task<IReadOnlyList<CurseForgeFileDto>> GetModFilesAsync(
        IReadOnlyCollection<int> fileIds,
        CancellationToken cancellationToken)
    {
        if (fileIds == null || fileIds.Count == 0)
        {
            return Array.Empty<CurseForgeFileDto>();
        }

        var response = await _client.PostAsync<List<CurseForgeFileData>>(
            "/v1/mods/files",
            new { fileIds },
            cancellationToken);

        return response.Data.Select(MapFile).ToList();
    }

    public async Task<IReadOnlyDictionary<int, string>> GetModFileDownloadUrlsAsync(
        IReadOnlyCollection<int> fileIds,
        CancellationToken cancellationToken)
    {
        if (fileIds == null || fileIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var response = await _client.PostAsync<List<CurseForgeDownloadUrlData>>(
            "/v1/mods/files/download-urls",
            new { fileIds },
            cancellationToken);

        return response.Data
            .Where(item => item.FileId.HasValue && !string.IsNullOrWhiteSpace(item.DownloadUrl))
            .ToDictionary(item => item.FileId!.Value, item => item.DownloadUrl!);
    }

    private static CurseForgeModSummaryDto MapModSummary(CurseForgeModData mod)
    {
        var categories = mod.Categories?.Select(MapCategory).ToList()
                        ?? new List<CurseForgeCategoryDto>();
        var latestFileId = mod.LatestFiles?.FirstOrDefault()?.Id;

        return new CurseForgeModSummaryDto(
            mod.Id,
            mod.Name ?? string.Empty,
            mod.Summary ?? string.Empty,
            mod.DownloadCount,
            mod.Logo?.ThumbnailUrl,
            latestFileId,
            categories);
    }

    private static CurseForgeModDto MapMod(CurseForgeModData mod)
    {
        var categories = mod.Categories?.Select(MapCategory).ToList()
                        ?? new List<CurseForgeCategoryDto>();
        var files = mod.LatestFiles?.Select(MapFile).ToList()
                    ?? new List<CurseForgeFileDto>();

        return new CurseForgeModDto(
            mod.Id,
            mod.Name ?? string.Empty,
            mod.Summary ?? string.Empty,
            mod.DownloadCount,
            mod.Logo?.ThumbnailUrl,
            categories,
            files);
    }

    private static CurseForgeFileDto MapFile(CurseForgeFileData file)
    {
        return new CurseForgeFileDto(
            file.Id,
            file.FileName ?? string.Empty,
            file.FileLength,
            file.FileDate,
            file.DownloadUrl,
            file.GameVersions ?? new List<string>(),
            file.ReleaseType,
            file.ServerPackFileId,
            file.IsServerPack);
    }

    private static CurseForgeCategoryDto MapCategory(CurseForgeCategoryData category)
    {
        return new CurseForgeCategoryDto(
            category.Id,
            category.Name ?? string.Empty,
            category.Url,
            category.IconUrl);
    }

    private static int? ResolveSortField(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return null;
        }

        return sort.Trim().ToLowerInvariant() switch
        {
            "popularity" => 2,
            "downloads" => 6,
            "updated" => 3,
            "name" => 4,
            _ => null
        };
    }

    private static int ResolveSortOrder(string? order, int sortField)
    {
        if (!string.IsNullOrWhiteSpace(order))
        {
            return order.Trim().ToLowerInvariant() == "asc" ? 2 : 1;
        }

        return sortField == 4 ? 2 : 1;
    }

    private sealed class CurseForgeModData
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Summary { get; set; }
        public long DownloadCount { get; set; }
        public CurseForgeLogoData? Logo { get; set; }
        public List<CurseForgeCategoryData>? Categories { get; set; }
        public List<CurseForgeFileData>? LatestFiles { get; set; }
    }

    private sealed class CurseForgeLogoData
    {
        public string? ThumbnailUrl { get; set; }
    }

    private sealed class CurseForgeCategoryData
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? IconUrl { get; set; }
    }

    private sealed class CurseForgeFileData
    {
        public int Id { get; set; }
        public string? FileName { get; set; }
        public long FileLength { get; set; }
        public DateTimeOffset FileDate { get; set; }
        public string? DownloadUrl { get; set; }
        public List<string>? GameVersions { get; set; }
        public int? ReleaseType { get; set; }
        public int? ServerPackFileId { get; set; }
        public bool? IsServerPack { get; set; }
    }

    private sealed class CurseForgeDownloadUrlData
    {
        public int? FileId { get; set; }
        public string? DownloadUrl { get; set; }
    }
}
