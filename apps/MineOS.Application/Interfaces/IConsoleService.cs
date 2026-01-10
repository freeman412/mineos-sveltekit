using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IConsoleService
{
    Task SendCommandAsync(string serverName, string command, CancellationToken cancellationToken);
    IAsyncEnumerable<LogEntryDto> StreamLogsAsync(string serverName, CancellationToken cancellationToken);
}
