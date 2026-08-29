// apps/MineOS.Application/Dtos/RedactionDtos.cs
namespace MineOS.Application.Dtos;

/// <summary>
/// Which optional redaction rules apply. The mandatory rules (IP addresses,
/// secret tokens, credential lines, email addresses) are not represented here
/// because they cannot be switched off.
/// </summary>
public record RedactionOptions(
    bool RedactPaths,
    bool RedactPlayerNames,
    IReadOnlyCollection<string> KnownPlayers);

public record RedactionResult(string Text, IReadOnlyList<string> RulesApplied);
