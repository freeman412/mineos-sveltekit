namespace MineOS.Application.Dtos;

public record ProfileDownloadProgressDto(
    long BytesDownloaded,
    long? TotalBytes,
    int Percentage,
    string Status
);
