using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

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

    private string GetStartupLogPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "logs", "startup.log");

    private string GetCrashReportsPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "crash-reports");

    public async Task SendCommandAsync(string serverName, string command, CancellationToken cancellationToken)
    {
        await _processManager.SendCommandAsync(
            serverName,
            command,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            cancellationToken);
        _logger.LogInformation("Sent command '{Command}' to server {ServerName}", command, serverName);
    }

    public Task ClearLogsAsync(string serverName, ConsoleLogSource source, CancellationToken cancellationToken)
    {
        var logPath = GetLogPath(serverName);
        var startupLogPath = GetStartupLogPath(serverName);

        switch (source)
        {
            case ConsoleLogSource.Java:
                TruncateLog(startupLogPath);
                break;
            case ConsoleLogSource.CrashReports:
                DeleteCrashReports(serverName);
                break;
            case ConsoleLogSource.Server:
                TruncateLog(logPath);
                break;
            case ConsoleLogSource.Combined:
            default:
                TruncateLog(logPath);
                TruncateLog(startupLogPath);
                break;
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<LogEntryDto> StreamLogsAsync(
        string serverName,
        ConsoleLogSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var logPath = GetLogPath(serverName);
        var startupLogPath = GetStartupLogPath(serverName);

        if (source == ConsoleLogSource.Server)
        {
            await foreach (var entry in StreamSingleLogAsync(
                               logPath,
                               "Waiting for server logs (latest.log not found yet). Start the server to create logs.",
                               null,
                               cancellationToken))
            {
                yield return entry;
            }

            yield break;
        }

        if (source == ConsoleLogSource.Java)
        {
            await foreach (var entry in StreamSingleLogAsync(
                               startupLogPath,
                               "Waiting for Java logs (startup.log not found yet). Start the server to create logs.",
                               null,
                               cancellationToken))
            {
                yield return entry;
            }

            yield break;
        }

        if (source == ConsoleLogSource.CrashReports)
        {
            await foreach (var entry in StreamCrashReportsAsync(serverName, cancellationToken))
            {
                yield return entry;
            }

            yield break;
        }

        var waitingNotified = false;
        FileStream? startupStream = null;
        StreamReader? startupReader = null;
        FileStream? serverStream = null;
        StreamReader? serverReader = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (startupReader == null && File.Exists(startupLogPath))
                {
                    foreach (var line in ReadLogTail(startupLogPath, 200))
                    {
                        yield return new LogEntryDto(DateTimeOffset.UtcNow, FormatLogLine("java", line));
                    }

                    startupStream = new FileStream(startupLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    startupReader = new StreamReader(startupStream);
                    startupReader.BaseStream.Seek(0, SeekOrigin.End);
                }

                if (serverReader == null && File.Exists(logPath))
                {
                    foreach (var line in ReadLogTail(logPath, 200))
                    {
                        yield return new LogEntryDto(DateTimeOffset.UtcNow, FormatLogLine("server", line));
                    }

                    serverStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    serverReader = new StreamReader(serverStream);
                    serverReader.BaseStream.Seek(0, SeekOrigin.End);
                }

                if (startupReader == null && serverReader == null)
                {
                    if (!waitingNotified)
                    {
                        waitingNotified = true;
                        yield return new LogEntryDto(
                            DateTimeOffset.UtcNow,
                            "Waiting for server logs (startup.log/latest.log not found yet). Start the server to create logs.");
                    }

                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                var hadLine = false;

                if (startupReader != null)
                {
                    var line = await TryReadLineAsync(startupReader, cancellationToken);
                    if (line != null)
                    {
                        hadLine = true;
                        yield return new LogEntryDto(DateTimeOffset.UtcNow, FormatLogLine("java", line));
                    }
                }

                if (serverReader != null)
                {
                    var line = await TryReadLineAsync(serverReader, cancellationToken);
                    if (line != null)
                    {
                        hadLine = true;
                        yield return new LogEntryDto(DateTimeOffset.UtcNow, FormatLogLine("server", line));
                    }
                }

                if (!hadLine)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
        finally
        {
            startupReader?.Dispose();
            startupStream?.Dispose();
            serverReader?.Dispose();
            serverStream?.Dispose();
        }
    }

    private async IAsyncEnumerable<LogEntryDto> StreamSingleLogAsync(
        string logPath,
        string waitingMessage,
        string? prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var waitingNotified = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!File.Exists(logPath))
            {
                if (!waitingNotified)
                {
                    waitingNotified = true;
                    yield return new LogEntryDto(DateTimeOffset.UtcNow, waitingMessage);
                }

                await Task.Delay(1000, cancellationToken);
                continue;
            }

            foreach (var line in ReadLogTail(logPath, 200))
            {
                yield return new LogEntryDto(DateTimeOffset.UtcNow, FormatLogLine(prefix, line));
            }

            FileStream? stream = null;
            StreamReader? reader = null;

            try
            {
                stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                reader = new StreamReader(stream);
                reader.BaseStream.Seek(0, SeekOrigin.End);
            }
            catch (IOException)
            {
                // Log file might rotate; loop will retry.
                stream?.Dispose();
                reader?.Dispose();
                waitingNotified = false;
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                // Log file may be temporarily locked; loop will retry.
                stream?.Dispose();
                reader?.Dispose();
                waitingNotified = false;
                continue;
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await TryReadLineAsync(reader, cancellationToken);
                    if (line != null)
                    {
                        yield return new LogEntryDto(DateTimeOffset.UtcNow, FormatLogLine(prefix, line));
                        continue;
                    }

                    if (!File.Exists(logPath))
                    {
                        break;
                    }

                    await Task.Delay(100, cancellationToken);
                }
            }
            finally
            {
                reader?.Dispose();
                stream?.Dispose();
            }

            waitingNotified = false;
        }
    }

    private static string FormatLogLine(string? prefix, string line)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return line;
        }

        return $"[{prefix}] {line}";
    }

    private async IAsyncEnumerable<LogEntryDto> StreamCrashReportsAsync(
        string serverName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var crashDir = GetCrashReportsPath(serverName);
        string? lastFile = null;
        DateTimeOffset? lastWrite = null;
        var waitingNotified = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!Directory.Exists(crashDir))
            {
                if (!waitingNotified)
                {
                    waitingNotified = true;
                    yield return new LogEntryDto(
                        DateTimeOffset.UtcNow,
                        "Waiting for crash reports (crash-reports folder not found yet).");
                }

                await Task.Delay(1000, cancellationToken);
                continue;
            }

            var latest = GetLatestCrashReport(crashDir);
            if (latest == null)
            {
                if (!waitingNotified)
                {
                    waitingNotified = true;
                    yield return new LogEntryDto(DateTimeOffset.UtcNow, "No crash reports found yet.");
                }

                await Task.Delay(1000, cancellationToken);
                continue;
            }

            waitingNotified = false;

            if (!string.Equals(latest.FullName, lastFile, StringComparison.OrdinalIgnoreCase) ||
                latest.LastWriteTimeUtc > (lastWrite?.UtcDateTime ?? DateTime.MinValue))
            {
                yield return new LogEntryDto(
                    DateTimeOffset.UtcNow,
                    $"=== Crash Report: {latest.Name} ===");

            IEnumerable<string> lines;
            string? readError = null;
            try
            {
                lines = File.ReadLines(latest.FullName);
            }
            catch (Exception ex)
            {
                readError = ex.Message;
                lines = Array.Empty<string>();
            }

            if (readError != null)
            {
                yield return new LogEntryDto(DateTimeOffset.UtcNow, $"Failed to read crash report: {readError}");
                await Task.Delay(1000, cancellationToken);
                continue;
            }

                foreach (var line in lines)
                {
                    yield return new LogEntryDto(DateTimeOffset.UtcNow, line);
                }

                lastFile = latest.FullName;
                lastWrite = latest.LastWriteTimeUtc;
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    private static FileInfo? GetLatestCrashReport(string crashDir)
    {
        try
        {
            var directory = new DirectoryInfo(crashDir);
            return directory.Exists
                ? directory.GetFiles("*.txt").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadLogTail(string path, int maxLines)
    {
        try
        {
            return File.ReadLines(path).Reverse().Take(maxLines).Reverse().ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void TruncateLog(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                OwnershipHelper.TrySetOwnership(directory, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
            }

            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            stream.SetLength(0);
            OwnershipHelper.TrySetOwnership(path, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
            _logger.LogInformation("Cleared log file {LogPath}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear log file {LogPath}", path);
        }
    }

    private void DeleteCrashReports(string serverName)
    {
        try
        {
            var crashDir = GetCrashReportsPath(serverName);
            if (!Directory.Exists(crashDir))
            {
                return;
            }

            var files = Directory.GetFiles(crashDir, "*.txt");
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete crash report {CrashReport}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear crash reports for {ServerName}", serverName);
        }
    }
}
