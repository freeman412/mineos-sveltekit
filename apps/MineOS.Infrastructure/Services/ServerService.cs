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

        if (string.IsNullOrEmpty(jarFile))
        {
            throw new InvalidOperationException($"Server '{name}' has no JAR file configured");
        }

        // Build screen command arguments
        var args = new List<string>
        {
            "-dmS",
            $"mc-{name}",
            javaBinary,
            "-server"
        };

        if (config.Java.JavaXmx > 0)
            args.Add($"-Xmx{config.Java.JavaXmx}M");
        if (config.Java.JavaXms > 0)
            args.Add($"-Xms{config.Java.JavaXms}M");

        if (!string.IsNullOrEmpty(config.Java.JavaTweaks))
        {
            args.AddRange(config.Java.JavaTweaks.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        args.Add("-jar");
        args.Add(jarFile);

        if (!string.IsNullOrEmpty(config.Java.JarArgs))
        {
            args.AddRange(config.Java.JarArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (!config.Minecraft.Unconventional)
        {
            args.Add("nogui");
        }

        // TODO: Get actual owner uid/gid
        var uid = 1000;
        var gid = 1000;

        await _processManager.StartScreenSessionAsync(name, args.ToArray(), uid, gid, serverPath, cancellationToken);

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
}
