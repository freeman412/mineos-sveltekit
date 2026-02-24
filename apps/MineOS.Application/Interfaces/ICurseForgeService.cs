using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface ICurseForgeService
{
    Task<CurseForgeSearchResultDto> SearchModsAsync(
        string query,
        int? classId,
        int index,
        int pageSize,
        string? sort,
        string? order,
        long? minDownloads,
        string? gameVersion,
        CancellationToken cancellationToken);

    Task<CurseForgeModDto> GetModAsync(int modId, CancellationToken cancellationToken);

    Task<CurseForgeFileDto> GetModFileAsync(int modId, int fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CurseForgeFileDto>> GetModFilesListAsync(
        int modId,
        string? gameVersion,
        CancellationToken cancellationToken);

    Task<string> GetModFileDownloadUrlAsync(int modId, int fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CurseForgeFileDto>> GetModFilesAsync(
        IReadOnlyCollection<int> fileIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, string>> GetModFileDownloadUrlsAsync(
        IReadOnlyCollection<int> fileIds,
        CancellationToken cancellationToken);
}
