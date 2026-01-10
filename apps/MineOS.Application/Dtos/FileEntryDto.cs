namespace MineOS.Application.Dtos;

public record FileEntryDto(string Name, bool IsDirectory, long Size, DateTimeOffset Modified);
