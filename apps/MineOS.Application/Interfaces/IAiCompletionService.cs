namespace MineOS.Application.Interfaces;

public enum AiFailureReason
{
    NotConfigured,
    Unauthorized,
    RateLimited,
    Timeout,
    BadResponse,
    Transport
}

public record AiCompletionRequest(string SystemPrompt, string UserPrompt, int? MaxTokens = null);

public record AiCompletionResult(
    bool Success,
    string? Content,
    string? Model,
    int? PromptTokens,
    int? CompletionTokens,
    AiFailureReason? Failure,
    string? ErrorMessage)
{
    public static AiCompletionResult Ok(string content, string model, int? promptTokens, int? completionTokens) =>
        new(true, content, model, promptTokens, completionTokens, null, null);

    public static AiCompletionResult Fail(AiFailureReason reason, string message) =>
        new(false, null, null, null, null, reason, message);
}

/// <summary>
/// A chat-completions endpoint speaking the OpenAI-compatible wire format.
/// Named for the protocol, not for any vendor.
/// </summary>
public interface IAiCompletionService
{
    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct);

    /// <summary>True when enabled with a base URL and model. The API key is optional.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct);
}
