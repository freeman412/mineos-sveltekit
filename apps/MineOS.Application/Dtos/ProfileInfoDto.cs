namespace MineOS.Application.Dtos;

public record ProfileInfoDto(
    string Id,
    string Name,
    string Version,
    string Type,
    string Url,
    bool Downloaded,
    long? Size
);
