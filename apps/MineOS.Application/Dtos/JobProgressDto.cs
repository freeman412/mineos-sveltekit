namespace MineOS.Application.Dtos;

public record JobProgressDto(
    string JobId,
    string Type,
    string ServerName,
    string Status,
    int Percentage,
    string? Message,
    DateTimeOffset Timestamp
);
