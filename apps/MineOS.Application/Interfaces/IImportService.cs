namespace MineOS.Application.Interfaces;

public interface IImportService
{
    Task<string> CreateServerFromImportAsync(string filename, string serverName, CancellationToken cancellationToken);
    Task<string> SaveImportAsync(string filename, Stream content, CancellationToken cancellationToken);
}
