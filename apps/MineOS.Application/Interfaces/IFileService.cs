using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IFileService
{
    Task<IReadOnlyList<FileEntryDto>> ListFilesAsync(string serverName, string path, CancellationToken cancellationToken);
    Task<FileContentDto> ReadFileAsync(string serverName, string path, CancellationToken cancellationToken);
    Task<byte[]> ReadFileBytesAsync(string serverName, string path, CancellationToken cancellationToken);
    Task WriteFileAsync(string serverName, string path, string content, CancellationToken cancellationToken);
    Task WriteFileBytesAsync(string serverName, string path, byte[] content, CancellationToken cancellationToken);
    Task DeleteFileAsync(string serverName, string path, CancellationToken cancellationToken);
}
