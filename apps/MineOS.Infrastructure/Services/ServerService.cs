using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public class ServerService : IServerService
{
    private readonly IProcessManager _processManager;
    private readonly HostOptions _options;
    private readonly ILogger<ServerService> _logger;

    public ServerService(
        IProcessManager processManager,
        IOptions<HostOptions> options,
        ILogger<ServerService> logger)
    {
        _processManager = processManager;
        _options = options.Value;
        _logger = logger;
    }

    private string GetServerPath(string name) =>
        Path.Combine(_options.BaseDirectory, _options.ServersPathSegment, name);

    private string GetBackupPath(string name) =>
        Path.Combine(_options.BaseDirectory, "backup", name);

    private string GetArchivePath(string name) =>
        Path.Combine(_options.BaseDirectory, "archive", name);

    private string GetPropertiesPath(string name) =>
        Path.Combine(GetServerPath(name), "server.properties");

    private string GetConfigPath(string name) =>
        Path.Combine(GetServerPath(name), "server.config");

    public async Task<ServerDetailDto> GetServerAsync(string name, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(name);

        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        var dirInfo = new DirectoryInfo(serverPath);
        var processInfo = _processManager.GetServerProcess(name);

        var config = await GetServerConfigAsync(name, cancellationToken);

        var status = processInfo?.JavaPid != null ? "running" : "stopped";

        // Get owner info from directory
        // On Linux, we'd use syscalls or a library to get uid/gid
        // For now, placeholder values
        var ownerUid = 1000; // TODO: Get actual owner
        var ownerGid = 1000;
        var ownerUsername = "user"; // TODO: Get actual username
        var ownerGroupname = "user"; // TODO: Get actual group

        return new ServerDetailDto(
            name,
            dirInfo.CreationTimeUtc,
            ownerUid,
            ownerGid,
            ownerUsername,
            ownerGroupname,
            status,
            processInfo?.JavaPid,
            processInfo?.ScreenPid,
            config
        );
    }

    public async Task<ServerDetailDto> CreateServerAsync(
        CreateServerRequest request,
        string username,
        CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(request.Name);
        var backupPath = GetBackupPath(request.Name);
        var archivePath = GetArchivePath(request.Name);

        if (Directory.Exists(serverPath))
        {
            throw new InvalidOperationException($"Server '{request.Name}' already exists");
        }

        // Create directories
        Directory.CreateDirectory(serverPath);
        Directory.CreateDirectory(backupPath);
        Directory.CreateDirectory(archivePath);

        // Create default files
        var propertiesPath = GetPropertiesPath(request.Name);
        var configPath = GetConfigPath(request.Name);

        // Write default server.properties
        var defaultProperties = new Dictionary<string, string>
        {
            ["server-port"] = "25565",
            ["max-players"] = "20",
            ["level-seed"] = "",
            ["gamemode"] = "0",
            ["difficulty"] = "1",
            ["level-type"] = "DEFAULT",
            ["level-name"] = "world",
            ["max-build-height"] = "256",
            ["generate-structures"] = "true",
            ["generator-settings"] = "",
            ["server-ip"] = "0.0.0.0",
            ["enable-query"] = "false"
        };

        await File.WriteAllTextAsync(propertiesPath, IniParser.WriteSimple(defaultProperties), cancellationToken);

        // Write default server.config
        var defaultConfig = new Dictionary<string, Dictionary<string, string>>
        {
            ["java"] = new()
            {
                ["java_binary"] = "",
                ["java_xmx"] = "256",
                ["java_xms"] = "256"
            },
            ["onreboot"] = new()
            {
                ["start"] = "false"
            }
        };

        await File.WriteAllTextAsync(configPath, IniParser.WriteWithSections(defaultConfig), cancellationToken);

        _logger.LogInformation("Created server {ServerName} at {ServerPath}", request.Name, serverPath);

        return await GetServerAsync(request.Name, cancellationToken);
    }

    public async Task DeleteServerAsync(string name, CancellationToken cancellationToken)
    {
        var isRunning = await _processManager.IsServerRunningAsync(name, cancellationToken);
        if (isRunning)
        {
            throw new InvalidOperationException($"Cannot delete running server '{name}'");
        }

        var serverPath = GetServerPath(name);
        var backupPath = GetBackupPath(name);
        var archivePath = GetArchivePath(name);

        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        Directory.Delete(serverPath, recursive: true);
        if (Directory.Exists(backupPath))
            Directory.Delete(backupPath, recursive: true);
        if (Directory.Exists(archivePath))
            Directory.Delete(archivePath, recursive: true);

        _logger.LogInformation("Deleted server {ServerName}", name);
    }

    public async Task<ServerHeartbeatDto> GetServerStatusAsync(string name, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(name);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        var processInfo = _processManager.GetServerProcess(name);
        var status = processInfo?.JavaPid != null ? "running" : "stopped";

        // TODO: Add ping info when we implement MinecraftPing protocol
        // TODO: Add memory info from /proc/{pid}/status

        return new ServerHeartbeatDto(
            name,
            status,
            processInfo?.JavaPid,
            processInfo?.ScreenPid,
            null, // ping
            null  // memory
        );
    }

    public async Task StartServerAsync(string name, CancellationToken cancellationToken)
    {
        _logger.LogInformation("StartServerAsync requested for {ServerName}", name);
        var isRunning = await _processManager.IsServerRunningAsync(name, cancellationToken);
        if (isRunning)
        {
            throw new InvalidOperationException($"Server '{name}' is already running");
        }

        var serverPath = GetServerPath(name);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        // Read server config to build start arguments
        var config = await GetServerConfigAsync(name, cancellationToken);
        var javaBinary = string.IsNullOrEmpty(config.Java.JavaBinary) ? "java" : config.Java.JavaBinary;
        var jarFile = config.Java.JarFile;

        _logger.LogInformation(
            "Server config for {ServerName}: javaBinary={JavaBinary} jarFile={JarFile} xmx={Xmx} xms={Xms}",
            name,
            javaBinary,
            jarFile ?? "<null>",
            config.Java.JavaXmx,
            config.Java.JavaXms);

        if (string.IsNullOrEmpty(jarFile))
        {
            throw new InvalidOperationException($"Server '{name}' has no JAR file configured");
        }

        EnsureEulaAccepted(serverPath);
        await EnsureExecutableAvailableAsync("screen", "-v", "GNU screen", cancellationToken);
        await EnsureExecutableAvailableAsync(javaBinary, "-version", "Java runtime", cancellationToken);

        var resolvedJarPath = ResolveJarPath(serverPath, jarFile);
        _logger.LogInformation("Resolved JAR path for {ServerName}: {JarPath}", name, resolvedJarPath);
        if (!File.Exists(resolvedJarPath))
        {
            throw new InvalidOperationException($"Configured JAR file not found: {resolvedJarPath}");
        }

        var logDir = Path.Combine(serverPath, "logs");
        Directory.CreateDirectory(logDir);
        var startupLogPath = Path.Combine(logDir, "startup.log");

        // Build Java command arguments
        var javaArgs = new List<string>
        {
            javaBinary,
            "-server",
            $"-Dmineos.server={name}"
        };

        if (config.Java.JavaXmx > 0)
            javaArgs.Add($"-Xmx{config.Java.JavaXmx}M");
        if (config.Java.JavaXms > 0)
            javaArgs.Add($"-Xms{config.Java.JavaXms}M");

        if (!string.IsNullOrEmpty(config.Java.JavaTweaks))
        {
            javaArgs.AddRange(config.Java.JavaTweaks.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        javaArgs.Add("-jar");
        javaArgs.Add(resolvedJarPath);

        if (!string.IsNullOrEmpty(config.Java.JarArgs))
        {
            javaArgs.AddRange(config.Java.JarArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (!config.Minecraft.Unconventional)
        {
            javaArgs.Add("nogui");
        }

        // TODO: Get actual owner uid/gid
        var uid = 1000;
        var gid = 1000;

        var startTime = DateTimeOffset.UtcNow;
        var startupStamp = $"[{startTime:O}] Launching {name}";
        var javaCommand = string.Join(" ", javaArgs.Select(EscapeBashArgument));
        var startupLogArg = EscapeBashArgument(startupLogPath);
        var shellCommand = $"echo {EscapeBashArgument(startupStamp)} >> {startupLogArg}; exec {javaCommand} >> {startupLogArg} 2>&1";

        var args = new List<string>
        {
            "-dmS",
            $"mc-{name}",
            "bash",
            "-lc",
            shellCommand
        };

        _logger.LogInformation(
            "Starting screen for {ServerName} in {WorkingDirectory}. Args: {Args}",
            name,
            serverPath,
            string.Join(' ', args));
        _logger.LogInformation("Startup log file: {StartupLogPath}", startupLogPath);
        _logger.LogInformation("Java command: {JavaCommand}", javaCommand);
        await _processManager.StartScreenSessionAsync(name, args.ToArray(), uid, gid, serverPath, cancellationToken);

        await VerifyServerStartedAsync(name, serverPath, startTime, startupLogPath, cancellationToken);
        _logger.LogInformation("Started server {ServerName}", name);
    }

    public async Task StopServerAsync(string name, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var isRunning = await _processManager.IsServerRunningAsync(name, cancellationToken);
        if (!isRunning)
        {
            throw new InvalidOperationException($"Server '{name}' is not running");
        }

        // TODO: Get actual owner uid/gid
        var uid = 1000;
        var gid = 1000;

        // Send stop command
        await _processManager.SendCommandAsync(name, "stop", uid, gid, cancellationToken);

        // Wait for process to stop
        var timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            await Task.Delay(200, cancellationToken);
            isRunning = await _processManager.IsServerRunningAsync(name, cancellationToken);
            if (!isRunning)
            {
                _logger.LogInformation("Server {ServerName} stopped gracefully", name);
                return;
            }
        }

        throw new TimeoutException($"Server '{name}' did not stop within {timeoutSeconds} seconds");
    }

    public async Task RestartServerAsync(string name, CancellationToken cancellationToken)
    {
        await StopServerAsync(name, 30, cancellationToken);
        await Task.Delay(1000, cancellationToken); // Brief pause between stop and start
        await StartServerAsync(name, cancellationToken);

        _logger.LogInformation("Restarted server {ServerName}", name);
    }

    public async Task KillServerAsync(string name, CancellationToken cancellationToken)
    {
        var processInfo = _processManager.GetServerProcess(name);
        if (processInfo?.JavaPid == null)
        {
            throw new InvalidOperationException($"Server '{name}' is not running");
        }

        await _processManager.KillProcessAsync(processInfo.JavaPid.Value, cancellationToken);

        _logger.LogWarning("Forcefully killed server {ServerName}", name);
    }

    public async Task<Dictionary<string, string>> GetServerPropertiesAsync(string name, CancellationToken cancellationToken)
    {
        var propertiesPath = GetPropertiesPath(name);
        if (!File.Exists(propertiesPath))
        {
            return new Dictionary<string, string>();
        }

        var content = await File.ReadAllTextAsync(propertiesPath, cancellationToken);
        return IniParser.ParseSimple(content);
    }

    public async Task UpdateServerPropertiesAsync(string name, Dictionary<string, string> properties, CancellationToken cancellationToken)
    {
        var propertiesPath = GetPropertiesPath(name);
        var content = IniParser.WriteSimple(properties);
        await File.WriteAllTextAsync(propertiesPath, content, cancellationToken);

        _logger.LogInformation("Updated server.properties for {ServerName}", name);
    }

    public async Task<ServerConfigDto> GetServerConfigAsync(string name, CancellationToken cancellationToken)
    {
        var configPath = GetConfigPath(name);
        if (!File.Exists(configPath))
        {
            // Return defaults
            return new ServerConfigDto(
                new JavaConfigDto("", 256, 256, null, null, null),
                new MinecraftConfigDto(null, false),
                new OnRebootConfigDto(false)
            );
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken);
        var sections = IniParser.ParseWithSections(content);

        var javaSection = sections.GetValueOrDefault("java", new Dictionary<string, string>());
        var minecraftSection = sections.GetValueOrDefault("minecraft", new Dictionary<string, string>());
        var onrebootSection = sections.GetValueOrDefault("onreboot", new Dictionary<string, string>());

        var java = new JavaConfigDto(
            javaSection.GetValueOrDefault("java_binary", ""),
            int.TryParse(javaSection.GetValueOrDefault("java_xmx", "256"), out var xmx) ? xmx : 256,
            int.TryParse(javaSection.GetValueOrDefault("java_xms", "256"), out var xms) ? xms : 256,
            javaSection.GetValueOrDefault("java_tweaks", null),
            javaSection.GetValueOrDefault("jarfile", null),
            javaSection.GetValueOrDefault("jar_args", null)
        );

        var minecraft = new MinecraftConfigDto(
            minecraftSection.GetValueOrDefault("profile", null),
            bool.TryParse(minecraftSection.GetValueOrDefault("unconventional", "false"), out var unconventional) && unconventional
        );

        var onreboot = new OnRebootConfigDto(
            bool.TryParse(onrebootSection.GetValueOrDefault("start", "false"), out var start) && start
        );

        return new ServerConfigDto(java, minecraft, onreboot);
    }

    public async Task UpdateServerConfigAsync(string name, ServerConfigDto config, CancellationToken cancellationToken)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>
        {
            ["java"] = new()
            {
                ["java_binary"] = config.Java.JavaBinary,
                ["java_xmx"] = config.Java.JavaXmx.ToString(),
                ["java_xms"] = config.Java.JavaXms.ToString(),
                ["java_tweaks"] = config.Java.JavaTweaks ?? "",
                ["jarfile"] = config.Java.JarFile ?? "",
                ["jar_args"] = config.Java.JarArgs ?? ""
            },
            ["minecraft"] = new()
            {
                ["profile"] = config.Minecraft.Profile ?? "",
                ["unconventional"] = config.Minecraft.Unconventional.ToString().ToLower()
            },
            ["onreboot"] = new()
            {
                ["start"] = config.OnReboot.Start.ToString().ToLower()
            }
        };

        var content = IniParser.WriteWithSections(sections);
        var configPath = GetConfigPath(name);
        await File.WriteAllTextAsync(configPath, content, cancellationToken);

        _logger.LogInformation("Updated server.config for {ServerName}", name);
    }

    public async Task AcceptEulaAsync(string name, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(name);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        var eulaPath = Path.Combine(serverPath, "eula.txt");
        var lines = File.Exists(eulaPath)
            ? new List<string>(await File.ReadAllLinesAsync(eulaPath, cancellationToken))
            : new List<string>();

        var updated = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("eula=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "eula=true";
                updated = true;
            }
        }

        if (!updated)
        {
            if (lines.Count == 0)
            {
                lines.Add("# Generated by MineOS");
            }
            lines.Add("eula=true");
        }

        await File.WriteAllLinesAsync(eulaPath, lines, cancellationToken);
        _logger.LogInformation("Accepted EULA for server {ServerName}", name);
    }

    public async Task RunFtbInstallerAsync(string name, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(name);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{name}' not found");
        }

        if (await _processManager.IsServerRunningAsync(name, cancellationToken))
        {
            throw new InvalidOperationException($"Server '{name}' must be stopped before running the FTB installer");
        }

        var installerPath = Path.Combine(serverPath, "FTBInstall.sh");
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException($"FTBInstall.sh not found in server '{name}'");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "sh",
            Arguments = "FTBInstall.sh",
            WorkingDirectory = serverPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start FTB installer");
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"FTB installer failed: {error}");
        }

        _logger.LogInformation("FTB installer completed for server {ServerName}", name);
    }

    private static string ResolveJarPath(string serverPath, string jarFile)
    {
        var trimmed = jarFile.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        return Path.Combine(serverPath, trimmed);
    }

    private static void EnsureEulaAccepted(string serverPath)
    {
        var eulaPath = Path.Combine(serverPath, "eula.txt");
        if (!File.Exists(eulaPath))
        {
            throw new InvalidOperationException("EULA has not been accepted yet. Call /servers/{name}/eula first.");
        }

        var lines = File.ReadAllLines(eulaPath);
        var eulaLine = lines.FirstOrDefault(line => line.TrimStart().StartsWith("eula=", StringComparison.OrdinalIgnoreCase));
        if (eulaLine == null || !eulaLine.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("EULA has not been accepted yet. Call /servers/{name}/eula first.");
        }
    }

    private async Task EnsureExecutableAvailableAsync(
        string executable,
        string args,
        string friendlyName,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking {FriendlyName} availability via {Executable} {Args}", friendlyName, executable, args);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                _logger.LogWarning("{FriendlyName} check failed: exitCode={ExitCode} error={Error}", friendlyName, process.ExitCode, error);
                throw new InvalidOperationException($"{friendlyName} check failed: {error}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"{friendlyName} is not available on PATH.", ex);
        }
    }

    private async Task VerifyServerStartedAsync(
        string name,
        string serverPath,
        DateTimeOffset startTime,
        string startupLogPath,
        CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(serverPath, "logs", "latest.log");
        var timeout = TimeSpan.FromSeconds(10);
        var started = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - started < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var processInfo = _processManager.GetServerProcess(name);
            if (processInfo?.ScreenPid != null || processInfo?.JavaPid != null)
            {
                _logger.LogInformation(
                    "Detected running process for {ServerName}: screenPid={ScreenPid} javaPid={JavaPid}",
                    name,
                    processInfo?.ScreenPid,
                    processInfo?.JavaPid);
                return;
            }

            if (File.Exists(logPath))
            {
                var logInfo = new FileInfo(logPath);
                if (logInfo.LastWriteTimeUtc >= startTime.UtcDateTime)
                {
                    _logger.LogInformation("Detected log output for {ServerName} at {LogPath}", name, logPath);
                    return;
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        _logger.LogWarning("Start verification timed out for {ServerName}. Log exists: {HasLog}", name, File.Exists(logPath));
        var latestLog = File.Exists(logPath) ? ReadLogTail(logPath, 20) : "No latest.log output found.";
        var startupLog = File.Exists(startupLogPath) ? ReadLogTail(startupLogPath, 40) : "No startup.log output found.";
        throw new InvalidOperationException($"Server '{name}' did not start. {latestLog} {startupLog}");
    }

    private static string ReadLogTail(string logPath, int maxLines)
    {
        try
        {
            var lines = File.ReadLines(logPath).Reverse().Take(maxLines).Reverse().ToList();
            return lines.Count > 0
                ? $"Last log lines: {string.Join(" | ", lines)}"
                : "Log file is empty.";
        }
        catch (Exception ex)
        {
            return $"Failed to read log output: {ex.Message}";
        }
    }

    private static string EscapeBashArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "''";
        }

        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }
}
