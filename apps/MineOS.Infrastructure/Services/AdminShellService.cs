using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class AdminShellService : IAdminShellSession
{
    private readonly ILogger<AdminShellService> _logger;
    private readonly HostOptions _options;

    public AdminShellService(
        ILogger<AdminShellService> logger,
        IOptions<HostOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task RunAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        if (socket == null)
        {
            throw new ArgumentNullException(nameof(socket));
        }

        using var process = StartShellProcess();
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start admin shell process.");
        }

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = linkedCts.Token;

        using var writer = process.StandardInput;
        writer.AutoFlush = true;

        var ttyPath = await ResolveTtyPathAsync(process, linkedToken);
        Func<int, int, CancellationToken, Task<bool>>? resizeHandler = null;
        if (!string.IsNullOrWhiteSpace(ttyPath))
        {
            resizeHandler = (cols, rows, token) => TryResizeTtyAsync(ttyPath, cols, rows, token);
        }

        var outputTask = PumpProcessOutputAsync(process.StandardOutput.BaseStream, socket, linkedToken);
        var errorTask = PumpProcessOutputAsync(process.StandardError.BaseStream, socket, linkedToken);
        var inputTask = PumpWebSocketInputAsync(socket, writer, resizeHandler, linkedToken);
        var exitTask = process.WaitForExitAsync(linkedToken);

        await Task.WhenAny(outputTask, errorTask, inputTask, exitTask);
        linkedCts.Cancel();

        try
        {
            await Task.WhenAll(outputTask, errorTask, inputTask, exitTask);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }

        try
        {
            if (!process.HasExited)
            {
                await writer.WriteAsync("exit\n");
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shell ended", CancellationToken.None);
        }
    }

    private Process StartShellProcess()
    {
        try
        {
            return StartWithScript();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start admin shell with script. Falling back to bash.");
            return StartWithBash();
        }
    }

    private Process StartWithScript()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "script",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _options.BaseDirectory
        };

        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("bash -i");
        startInfo.ArgumentList.Add("/dev/null");

        startInfo.Environment["TERM"] = "xterm-256color";

        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start script process.");
        }

        return process;
    }

    private static Process StartWithBash()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = "-i",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["TERM"] = "xterm-256color";

        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start bash process.");
        }

        return process;
    }

    private static async Task PumpProcessOutputAsync(Stream stream, WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var decoder = Encoding.UTF8.GetDecoder();
        var charBuffer = new char[8192];

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            var charsRead = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0);
            if (charsRead == 0)
            {
                continue;
            }

            var payload = Encoding.UTF8.GetBytes(charBuffer, 0, charsRead);
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
        }
    }

    private static async Task PumpWebSocketInputAsync(
        WebSocket socket,
        StreamWriter writer,
        Func<int, int, CancellationToken, Task<bool>>? resizeHandler,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            using var messageBuffer = new MemoryStream();
            messageBuffer.Write(buffer, 0, result.Count);

            while (!result.EndOfMessage)
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                messageBuffer.Write(buffer, 0, result.Count);
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var text = Encoding.UTF8.GetString(messageBuffer.ToArray());
                if (await TryHandleResizeAsync(text, resizeHandler, cancellationToken))
                {
                    continue;
                }
                await writer.WriteAsync(text);
                continue;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                messageBuffer.Position = 0;
                await messageBuffer.CopyToAsync(writer.BaseStream, cancellationToken);
                await writer.FlushAsync();
            }
        }
    }

    private static async Task<bool> TryHandleResizeAsync(
        string text,
        Func<int, int, CancellationToken, Task<bool>>? resizeHandler,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 128 || text[0] != '{')
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("type", out var typeElement) ||
                typeElement.GetString() != "resize")
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("cols", out var colsElement) ||
                !document.RootElement.TryGetProperty("rows", out var rowsElement))
            {
                return false;
            }

            var cols = colsElement.GetInt32();
            var rows = rowsElement.GetInt32();
            if (cols <= 0 || rows <= 0)
            {
                return false;
            }

            if (resizeHandler != null)
            {
                await resizeHandler(cols, rows, cancellationToken);
                return true;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
    }

    private static async Task<string?> ResolveTtyPathAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var pid = process.Id;
        var attempts = 0;
        while (!cancellationToken.IsCancellationRequested && attempts < 6)
        {
            var direct = TryResolveTtyPathForPid(pid);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            foreach (var childPid in GetChildPids(pid))
            {
                var childTty = TryResolveTtyPathForPid(childPid);
                if (!string.IsNullOrWhiteSpace(childTty))
                {
                    return childTty;
                }
            }

            attempts += 1;
            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    private static string? TryResolveTtyPathForPid(int pid)
    {
        var fdCandidates = new[] { "0", "1", "2" };
        foreach (var fd in fdCandidates)
        {
            var path = $"/proc/{pid}/fd/{fd}";
            try
            {
                var info = new FileInfo(path);
                var target = info.ResolveLinkTarget(true);
                var fullPath = target?.FullName;
                if (!string.IsNullOrWhiteSpace(fullPath) && fullPath.StartsWith("/dev/pts/"))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore and continue.
            }
        }

        return null;
    }

    private static IEnumerable<int> GetChildPids(int pid)
    {
        var childrenPath = $"/proc/{pid}/task/{pid}/children";
        if (!File.Exists(childrenPath))
        {
            return Array.Empty<int>();
        }

        try
        {
            var content = File.ReadAllText(childrenPath);
            if (string.IsNullOrWhiteSpace(content))
            {
                return Array.Empty<int>();
            }

            var ids = content
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out var id) ? id : -1)
                .Where(id => id > 0)
                .ToList();

            return ids;
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private static async Task<bool> TryResizeTtyAsync(
        string ttyPath,
        int cols,
        int rows,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ttyPath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "stty",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-F");
            startInfo.ArgumentList.Add(ttyPath);
            startInfo.ArgumentList.Add("cols");
            startInfo.ArgumentList.Add(cols.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("rows");
            startInfo.ArgumentList.Add(rows.ToString(CultureInfo.InvariantCulture));

            using var resizeProcess = Process.Start(startInfo);
            if (resizeProcess == null)
            {
                return false;
            }

            await resizeProcess.WaitForExitAsync(cancellationToken);
            return resizeProcess.ExitCode == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
