using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Services;

namespace MineOS.Infrastructure.External;

/// <summary>
/// Talks to any endpoint speaking the OpenAI-compatible /chat/completions
/// format. Named for the protocol, not for a vendor.
/// </summary>
public sealed class OpenAiCompatibleClient : IAiCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ILogger<OpenAiCompatibleClient> _logger;

    public OpenAiCompatibleClient(
        HttpClient httpClient,
        ISettingsService settings,
        ILogger<OpenAiCompatibleClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public static string BuildRequestPayload(AiCompletionRequest request, string model)
    {
        var payload = new
        {
            model,
            max_tokens = request.MaxTokens,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct)
    {
        // The API key is deliberately not part of this test: local endpoints take none.
        var enabled = await _settings.GetAsync(SettingsService.Keys.AiEnabled, ct);
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)) return false;

        var baseUrl = await _settings.GetAsync(SettingsService.Keys.AiBaseUrl, ct);
        var model = await _settings.GetAsync(SettingsService.Keys.AiModel, ct);
        return !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(model);
    }

    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct)
    {
        if (!await IsConfiguredAsync(ct))
        {
            return AiCompletionResult.Fail(AiFailureReason.NotConfigured,
                "AI is not configured. Set an endpoint URL and model in Settings.");
        }

        var baseUrl = (await _settings.GetAsync(SettingsService.Keys.AiBaseUrl, ct))!.TrimEnd('/');
        var model = (await _settings.GetAsync(SettingsService.Keys.AiModel, ct))!;
        var apiKey = await _settings.GetAsync(SettingsService.Keys.AiApiKey, ct);
        var maxTokens = request.MaxTokens
            ?? ParseInt(await _settings.GetAsync(SettingsService.Keys.AiMaxTokens, ct), 1000);
        var timeout = ParseInt(await _settings.GetAsync(SettingsService.Keys.AiTimeoutSeconds, ct), 60);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(
                BuildRequestPayload(request with { MaxTokens = maxTokens }, model),
                Encoding.UTF8, "application/json")
        };

        // Only send Authorization when a key exists — local endpoints reject it.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiCompletionResult.Fail(AiFailureReason.Timeout, $"The endpoint did not respond within {timeout}s.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("AI endpoint unreachable: {Message}", ex.Message);
            return AiCompletionResult.Fail(AiFailureReason.Transport, "Could not reach the AI endpoint.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Never log the body: it can echo the prompt.
                _logger.LogWarning("AI endpoint returned {Status} for model {Model}", (int)response.StatusCode, model);
                return AiCompletionResult.Fail(MapStatus(response.StatusCode), response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "The AI endpoint rejected the API key. Check it in Settings.",
                    HttpStatusCode.TooManyRequests => "The AI endpoint is rate limiting requests. Try again shortly.",
                    _ => $"The AI endpoint returned HTTP {(int)response.StatusCode}."
                });
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return AiCompletionResult.Fail(AiFailureReason.BadResponse, "The AI endpoint returned an empty response.");
                }

                int? promptTokens = null, completionTokens = null;
                if (root.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("prompt_tokens", out var p)) promptTokens = p.GetInt32();
                    if (usage.TryGetProperty("completion_tokens", out var c)) completionTokens = c.GetInt32();
                }

                var reportedModel = root.TryGetProperty("model", out var m) ? m.GetString() ?? model : model;
                _logger.LogInformation("AI diagnosis completed with {Model} ({Prompt}+{Completion} tokens)",
                    reportedModel, promptTokens, completionTokens);
                return AiCompletionResult.Ok(content, reportedModel, promptTokens, completionTokens);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                return AiCompletionResult.Fail(AiFailureReason.BadResponse,
                    "The AI endpoint returned a response MineOS could not read.");
            }
        }
    }

    private static AiFailureReason MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailureReason.Unauthorized,
        HttpStatusCode.TooManyRequests => AiFailureReason.RateLimited,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => AiFailureReason.Timeout,
        _ => AiFailureReason.Transport
    };

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;
}
