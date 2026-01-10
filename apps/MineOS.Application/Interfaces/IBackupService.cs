using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IBackupService
{
    Task<IEnumerable<IncrementEntryDto>> ListBackupsAsync(string serverName, CancellationToken cancellationToken);
    Task CreateBackupAsync(string serverName, CancellationToken cancellationToken);
    Task RestoreBackupAsync(string serverName, string timestamp, CancellationToken cancellationToken);
    Task PruneBackupsAsync(string serverName, int keepCount, CancellationToken cancellationToken);
    Task<IEnumerable<IncrementEntryDto>> GetBackupSizesAsync(string serverName, CancellationToken cancellationToken);
}
