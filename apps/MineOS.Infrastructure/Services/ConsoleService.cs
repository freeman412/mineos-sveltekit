using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class ConsoleService : IConsoleService
{
    private readonly ILogger<ConsoleService> _logger;
    private readonly IProcessManager _processManager;
    private readonly HostOptions _hostOptions;

    public ConsoleService(
        ILogger<ConsoleService> logger,
        IProcessManager processManager,
        IOptions<HostOptions> hostOptions)
    {
        _logger = logger;
        _processManager = processManager;
        _hostOptions = hostOptions.Value;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetLogPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "logs", "latest.log");

    public async Task SendCommandAsync(string serverName, string command, CancellationToken cancellationToken)
    {
        // TODO: Get UID/GID from server ownership
        await _processManager.SendCommandAsync(serverName, command, 1000, 1000, cancellationToken);
        _logger.LogInformation("Sent command '{Command}' to server {ServerName}", command, serverName);
    }

    public async IAsyncEnumerable<LogEntryDto> StreamLogsAsync(
        string serverName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var logPath = GetLogPath(serverName);

        // If log file doesn't exist yet, wait for it
        while (!File.Exists(logPath) && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
        }

        if (!File.Exists(logPath))
        {
            yield break;
        }

        using var fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fileStream);

        // Seek to end to only get new lines
        reader.BaseStream.Seek(0, SeekOrigin.End);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line != null)
            {
                yield return new LogEntryDto(DateTimeOffset.UtcNow, line);
            }
            else
            {
                // No new data, wait before checking again
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}
