namespace MineOS.Application.Dtos;

public record InstalledPluginDto(
    string FileName,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    bool IsDisabled
);
