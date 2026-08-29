// apps/MineOS.Application/Dtos/DiagnosisDtos.cs
namespace MineOS.Application.Dtos;

public record DiagnosisPreviewDto(
    string RedactedInput,
    int ApproxCharacters,
    IReadOnlyList<string> RulesApplied);

public record CrashDiagnosisDto(
    int Id,
    int CrashEventId,
    string ServerName,
    DateTimeOffset CreatedAt,
    string Model,
    string? Summary,
    string? LikelyCause,
    IReadOnlyList<string> SuggestedActions,
    string? Classification,
    string? Confidence,
    string Status,
    string? Error);
