// apps/MineOS.Domain/Entities/CrashDiagnosis.cs
namespace MineOS.Domain.Entities;

public sealed class CrashDiagnosis
{
    public int Id { get; set; }
    public int CrashEventId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>SHA-256 of the redacted input plus the model. Unique per server.</summary>
    public string SourceHash { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>Exactly what was sent — the audit trail for what left the machine.</summary>
    public string RedactedInput { get; set; } = string.Empty;

    public string? Summary { get; set; }
    public string? LikelyCause { get; set; }

    /// <summary>JSON array of suggested action strings.</summary>
    public string? SuggestedActions { get; set; }

    /// <summary>mineos-bug | mod-or-modpack | environment | unknown</summary>
    public string? Classification { get; set; }

    /// <summary>low | medium | high</summary>
    public string? Confidence { get; set; }

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    /// <summary>pending | complete | failed</summary>
    public string Status { get; set; } = string.Empty;

    public string? Error { get; set; }
}
