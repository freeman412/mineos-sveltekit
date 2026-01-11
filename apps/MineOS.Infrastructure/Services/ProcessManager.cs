using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public partial class ProcessManager : IProcessManager
{
    private readonly ILogger<ProcessManager> _logger;
    private const string PROC_PATH = "/proc";
    private const string ScreenCommand = "screen";

    [GeneratedRegex(@"screen[^S]+S mc-([^\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenRegex();

    // Matches the full argument when cmdline is split by \0
    [GeneratedRegex(@"^-Dmineos\.server=(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex JavaRegex();

    // Matches environment variable when environ is split by \0
    [GeneratedRegex(@"^MINEOS_SERVER=(.+)$", RegexOptions.IgnoreCase)]
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

                var cmdlineRaw = File.ReadAllText(cmdlinePath);
                // Split by null bytes to get individual arguments
                var cmdlineArgs = cmdlineRaw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                // Also create joined version for screen regex (which matches across args)
                var cmdlineJoined = string.Join(" ", cmdlineArgs);

                // Check for screen session (uses joined cmdline)
                var screenMatch = ScreenRegex().Match(cmdlineJoined);
                if (screenMatch.Success)
                {
                    var serverName = screenMatch.Groups[1].Value;
                    if (!servers.ContainsKey(serverName))
                        servers[serverName] = new ServerProcessInfo(null, null);

                    servers[serverName] = servers[serverName] with { ScreenPid = int.Parse(pidStr) };
                    continue;
                }

                // Check for Java process with MineOS identifier in cmdline
                // Check each argument individually to handle server names with spaces
                foreach (var arg in cmdlineArgs)
                {
                    var javaMatch = JavaRegex().Match(arg);
                    if (javaMatch.Success)
                    {
                        var serverName = javaMatch.Groups[1].Value;
                        if (!servers.ContainsKey(serverName))
                            servers[serverName] = new ServerProcessInfo(null, null);

                        servers[serverName] = servers[serverName] with { JavaPid = int.Parse(pidStr) };
                        break;
                    }
                }

                // Check for environment variable identifier
                var environPath = Path.Combine(PROC_PATH, pidStr, "environ");
                if (!File.Exists(environPath))
                    continue;

                var environRaw = File.ReadAllText(environPath);
                var environVars = environRaw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                foreach (var envVar in environVars)
                {
                    var envMatch = EnvServerRegex().Match(envVar);
                    if (envMatch.Success)
                    {
                        var serverName = envMatch.Groups[1].Value;
                        if (!servers.ContainsKey(serverName))
                            servers[serverName] = new ServerProcessInfo(null, null);

                        servers[serverName] = servers[serverName] with { JavaPid = int.Parse(pidStr) };
                        break;
                    }
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
        var startInfo = BuildProcessStartInfo(ScreenCommand, args, uid, gid);
        startInfo.WorkingDirectory = workingDirectory;

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
        await SendScreenCommandAsync($"mc-{serverName}", command, uid, gid, cancellationToken);
    }

    public async Task SendScreenCommandAsync(
        string sessionName,
        string command,
        int uid,
        int gid,
        CancellationToken cancellationToken)
    {
        var args = new[]
        {
            "-S",
            sessionName,
            "-p",
            "0",
            "-X",
            "eval",
            $"stuff \"{command}\\012\""
        };

        var startInfo = BuildProcessStartInfo(ScreenCommand, args, uid, gid);

        _logger.LogDebug("Sending command to screen session {Session}: {Command}", sessionName, command);

        var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send command to screen session: {error}");
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

    private ProcessStartInfo BuildProcessStartInfo(string command, string[] args, int uid, int gid)
    {
        var useSu = ShouldUseSu(uid, gid);
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (useSu)
        {
            // Use su to run command as unprivileged minecraft user
            // su provides proper environment initialization for screen sessions
            startInfo.FileName = "/bin/su";
            startInfo.ArgumentList.Add("-");  // Login shell
            startInfo.ArgumentList.Add("minecraft");  // Username
            startInfo.ArgumentList.Add("-c");  // Execute command

            // Build the full command string with arguments
            var escapedArgs = args.Select(a => $"'{a.Replace("'", "'\\''")}'");
            var fullCommand = command + " " + string.Join(" ", escapedArgs);
            startInfo.ArgumentList.Add(fullCommand);
        }
        else
        {
            startInfo.FileName = command;
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        return startInfo;
    }

    private static bool ShouldUseSu(int uid, int gid)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        var effectiveUid = TryGetEffectiveUid();
        if (effectiveUid != 0)
        {
            return false;
        }

        return uid > 0 || gid > 0;
    }

    private static int? TryGetEffectiveUid()
    {
        const string statusPath = "/proc/self/status";
        if (!File.Exists(statusPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(statusPath))
        {
            if (!line.StartsWith("Uid:", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var uid))
            {
                return uid;
            }
        }

        return null;
    }

    private static string? ResolveSetprivPath()
    {
        var candidates = new[]
        {
            "/usr/bin/setpriv",
            "/bin/setpriv"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
