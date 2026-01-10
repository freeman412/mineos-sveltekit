using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IArchiveService
{
    Task<IEnumerable<ArchiveEntryDto>> ListArchivesAsync(string serverName, CancellationToken cancellationToken);
    Task<string> CreateArchiveAsync(string serverName, CancellationToken cancellationToken);
    Task DeleteArchiveAsync(string serverName, string filename, CancellationToken cancellationToken);
    Task<string> GetArchivePathAsync(string serverName, string filename, CancellationToken cancellationToken);
}
