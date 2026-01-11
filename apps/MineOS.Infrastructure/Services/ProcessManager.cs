using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public partial class ProcessManager : IProcessManager
{
    private readonly ILogger<ProcessManager> _logger;
    private const string PROC_PATH = "/proc";

    [GeneratedRegex(@"screen[^S]+S mc-([^\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenRegex();

    [GeneratedRegex(@"-Dmineos\.server=([^\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex JavaRegex();

    [GeneratedRegex(@"MINEOS_SERVER=([^\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EnvServerRegex();

    public ProcessManager(ILogger<ProcessManager> _logger)
    {
        this._logger = _logger;
    }

    public Dictionary<string, ServerProcessInfo> GetServerProcesses()
    {
        var servers = new Dictionary<string, ServerProcessInfo>();

        if (!Directory.Exists(PROC_PATH))
        {
            _logger.LogWarning("/proc filesystem not found - process discovery disabled");
            return servers;
        }

        var pids = Directory.GetDirectories(PROC_PATH)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && int.TryParse(name, out _))
            .ToList();

        foreach (var pidStr in pids)
        {
            try
            {
                var cmdlinePath = Path.Combine(PROC_PATH, pidStr, "cmdline");
                if (!File.Exists(cmdlinePath))
                    continue;

                var cmdline = File.ReadAllText(cmdlinePath).Replace("\0", " ");

                // Check for screen session
                var screenMatch = ScreenRegex().Match(cmdline);
                if (screenMatch.Success)
                {
                    var serverName = screenMatch.Groups[1].Value;
                    if (!servers.ContainsKey(serverName))
                        servers[serverName] = new ServerProcessInfo(null, null);

                    servers[serverName] = servers[serverName] with { ScreenPid = int.Parse(pidStr) };
                    continue;
                }

                // Check for Java process with MineOS identifier in cmdline
                var javaMatch = JavaRegex().Match(cmdline);
                if (javaMatch.Success)
                {
                    var serverName = javaMatch.Groups[1].Value;
                    if (!servers.ContainsKey(serverName))
                        servers[serverName] = new ServerProcessInfo(null, null);

                    servers[serverName] = servers[serverName] with { JavaPid = int.Parse(pidStr) };
                    continue;
                }

                // Check for environment variable identifier
                var environPath = Path.Combine(PROC_PATH, pidStr, "environ");
                if (!File.Exists(environPath))
                    continue;

                var environ = File.ReadAllText(environPath).Replace("\0", " ");
                var envMatch = EnvServerRegex().Match(environ);
                if (envMatch.Success)
                {
                    var serverName = envMatch.Groups[1].Value;
                    if (!servers.ContainsKey(serverName))
                        servers[serverName] = new ServerProcessInfo(null, null);

                    servers[serverName] = servers[serverName] with { JavaPid = int.Parse(pidStr) };
                }
            }
            catch (Exception ex)
            {
                // Process might have exited between directory enumeration and file read
                _logger.LogTrace(ex, "Failed to read process info for PID {Pid}", pidStr);
            }
        }

        return servers;
    }

    public ServerProcessInfo? GetServerProcess(string serverName)
    {
        var processes = GetServerProcesses();
        return processes.TryGetValue(serverName, out var info) ? info : null;
    }

    public async Task StartScreenSessionAsync(
        string serverName,
        string[] args,
        int uid,
        int gid,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "screen",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Add all arguments
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        _logger.LogInformation("Starting screen session for {ServerName} as uid={Uid} gid={Gid}",
            serverName, uid, gid);
        _logger.LogInformation("screen command args: {Args}", string.Join(" ", args));

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Failed to start screen: 'screen' is not available on PATH.", ex);
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            _logger.LogWarning(
                "screen exited with code {ExitCode} for {ServerName}. stdout={Stdout} stderr={Stderr}",
                process.ExitCode,
                serverName,
                output,
                error);
            throw new InvalidOperationException($"Failed to start screen session: {error}");
        }

        // Give the screen session a moment to initialize
        await Task.Delay(100, cancellationToken);
    }

    public async Task SendCommandAsync(
        string serverName,
        string command,
        int uid,
        int gid,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "screen",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-S");
        startInfo.ArgumentList.Add($"mc-{serverName}");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add("eval");
        startInfo.ArgumentList.Add($"stuff \"{command}\\012\"");

        _logger.LogDebug("Sending command to {ServerName}: {Command}", serverName, command);

        var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send command to server: {error}");
        }
    }

    public Task<bool> IsServerRunningAsync(string serverName, CancellationToken cancellationToken)
    {
        var processes = GetServerProcesses();
        var isRunning = processes.ContainsKey(serverName);
        return Task.FromResult(isRunning);
    }

    public async Task KillProcessAsync(int pid, CancellationToken cancellationToken)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill();
            await process.WaitForExitAsync(cancellationToken);
            _logger.LogInformation("Killed process {Pid}", pid);
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            _logger.LogWarning("Process {Pid} not found", pid);
        }
    }
}
