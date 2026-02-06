namespace MineOS.Application.Dtos;

public record DeviceCodeResponse(
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresIn
);

public record LinkStatus(
    string Status,
    string? UserCode,
    string? VerificationUri,
    string? VerificationUriComplete,
    int? ExpiresIn,
    LinkedAccountInfo? LinkedAccount,
    string? Error
);

public record LinkedAccountInfo(
    string UserId,
    DateTimeOffset LinkedAt,
    DateTimeOffset ExpiresAt
);
