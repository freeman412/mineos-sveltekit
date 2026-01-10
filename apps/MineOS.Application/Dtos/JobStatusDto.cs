namespace MineOS.Application.Dtos;

public record JobStatusDto(
    string JobId,
    string Type,
    string ServerName,
    string Status,
    int Percentage,
    string? Message,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error
);
