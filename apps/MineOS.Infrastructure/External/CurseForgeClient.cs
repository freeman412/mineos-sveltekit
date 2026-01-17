using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Services;

namespace MineOS.Infrastructure.External;

public sealed class CurseForgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly CurseForgeOptions _options;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<CurseForgeClient> _logger;

    public CurseForgeClient(
        HttpClient httpClient,
        IOptions<CurseForgeOptions> options,
        ISettingsService settingsService,
        ILogger<CurseForgeClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _settingsService = settingsService;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            var normalized = NormalizeBaseUrl(_options.BaseUrl);
            if (!string.Equals(normalized, _options.BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Normalizing CurseForge base URL from {BaseUrl} to {Normalized}", _options.BaseUrl, normalized);
            }

            _httpClient.BaseAddress = new Uri(normalized);
        }
    }

    public async Task<CurseForgeApiResponse<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var apiKey = await GetApiKeyAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("x-api-key", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var url = request.RequestUri?.ToString() ?? path;
            var retryAfter = GetHeader(response, "retry-after");
            var rateRemaining = GetHeader(response, "x-ratelimit-remaining") ?? GetHeader(response, "x-rate-limit-remaining");
            var rateLimit = GetHeader(response, "x-ratelimit-limit") ?? GetHeader(response, "x-rate-limit-limit");
            var rateReset = GetHeader(response, "x-ratelimit-reset") ?? GetHeader(response, "x-rate-limit-reset");
            var body = string.IsNullOrWhiteSpace(payload) ? "<empty>" : payload;

            _logger.LogWarning(
                "CurseForge API error ({Status}) for {Url}. Retry-After={RetryAfter} RateLimit={RateLimit} RateRemaining={RateRemaining} RateReset={RateReset} Body={Body}",
                response.StatusCode,
                url,
                retryAfter,
                rateLimit,
                rateRemaining,
                rateReset,
                TrimBody(body));

            throw new HttpRequestException(
                $"CurseForge API error ({(int)response.StatusCode}) for {url}: {TrimBody(body)}",
                null,
                response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<CurseForgeApiResponse<T>>(payload, JsonOptions);
        if (parsed == null)
        {
            throw new InvalidOperationException("CurseForge API response was empty");
        }

        return parsed;
    }

    public async Task<CurseForgeApiResponse<T>> PostAsync<T>(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        var apiKey = await GetApiKeyAsync(cancellationToken);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("x-api-key", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var url = request.RequestUri?.ToString() ?? path;
            var retryAfter = GetHeader(response, "retry-after");
            var rateRemaining = GetHeader(response, "x-ratelimit-remaining") ?? GetHeader(response, "x-rate-limit-remaining");
            var rateLimit = GetHeader(response, "x-ratelimit-limit") ?? GetHeader(response, "x-rate-limit-limit");
            var rateReset = GetHeader(response, "x-ratelimit-reset") ?? GetHeader(response, "x-rate-limit-reset");
            var payloadBody = string.IsNullOrWhiteSpace(body) ? "<empty>" : body;

            _logger.LogWarning(
                "CurseForge API error ({Status}) for {Url}. Retry-After={RetryAfter} RateLimit={RateLimit} RateRemaining={RateRemaining} RateReset={RateReset} Body={Body}",
                response.StatusCode,
                url,
                retryAfter,
                rateLimit,
                rateRemaining,
                rateReset,
                TrimBody(payloadBody));

            throw new HttpRequestException(
                $"CurseForge API error ({(int)response.StatusCode}) for {url}: {TrimBody(payloadBody)}",
                null,
                response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<CurseForgeApiResponse<T>>(body, JsonOptions);
        if (parsed == null)
        {
            throw new InvalidOperationException("CurseForge API response was empty");
        }

        return parsed;
    }

    private async Task<string> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        var apiKey = await _settingsService.GetAsync(SettingsService.Keys.CurseForgeApiKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("CurseForge API key is not configured. Please configure it in Admin > Settings.");
        }
        return apiKey;
    }

    /// <summary>
    /// Check if the CurseForge API key is configured (database or config)
    /// </summary>
    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken)
    {
        return _settingsService.HasValueAsync(SettingsService.Keys.CurseForgeApiKey, cancellationToken);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3];
        }
        else if (trimmed.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed;
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return string.Join(",", values);
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            return string.Join(",", contentValues);
        }

        return null;
    }

    private static string TrimBody(string body)
    {
        const int max = 512;
        if (body.Length <= max)
        {
            return body;
        }

        return body[..max] + "...";
    }
}

public sealed class CurseForgeApiResponse<T>
{
    public T Data { get; set; } = default!;
    public CurseForgePagination? Pagination { get; set; }
}

public sealed class CurseForgePagination
{
    public int Index { get; set; }
    public int PageSize { get; set; }
    public int ResultCount { get; set; }
    public int TotalCount { get; set; }
}
