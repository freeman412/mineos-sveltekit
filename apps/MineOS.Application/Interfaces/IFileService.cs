using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IFileService
{
    Task<IEnumerable<FileEntryDto>> ListFilesAsync(string serverName, string path, CancellationToken cancellationToken);
    Task<string> ReadFileAsync(string serverName, string path, CancellationToken cancellationToken);
    Task WriteFileAsync(string serverName, string path, string content, CancellationToken cancellationToken);
    Task DeleteFileAsync(string serverName, string path, CancellationToken cancellationToken);
}
