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
    private const string RestartFlagFile = ".mineos-restart-required";
    private const string StopRequestedFlagFile = ".mineos-stop-requested";
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

    private string GetRestartFlagPath(string name) =>
        Path.Combine(GetServerPath(name), RestartFlagFile);

    private string GetStopRequestedFlagPath(string name) =>
        Path.Combine(GetServerPath(name), StopRequestedFlagFile);

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
        var eulaAccepted = await IsEulaAcceptedAsync(serverPath, cancellationToken);
        var needsRestart = File.Exists(GetRestartFlagPath(name));

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
            config,
            eulaAccepted,
            needsRestart
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

        var usedPorts = await GetUsedPortsAsync(excludeName: null, cancellationToken);
        var defaultPort = GetNextAvailablePort(usedPorts, 25565);

        // Write default server.properties
        var defaultProperties = new Dictionary<string, string>
        {
            ["server-port"] = defaultPort.ToString(),
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
            ["enable-query"] = "false",
            ["white-list"] = "true",
            ["enforce-whitelist"] = "true"
        };

        await File.WriteAllTextAsync(propertiesPath, IniParser.WriteSimple(defaultProperties), cancellationToken);

        // Write default server.config
        var defaultConfig = new Dictionary<string, Dictionary<string, string>>
        {
            ["java"] = new()
            {
                ["java_binary"] = "",
                ["java_xmx"] = "4096",
                ["java_xms"] = "4096"
            },
            ["minecraft"] = new()
            {
                ["profile"] = "",
                ["unconventional"] = "false",
                ["lan_broadcast"] = "false"
            },
            ["onreboot"] = new()
            {
                ["start"] = "false"
            }
        };

        await File.WriteAllTextAsync(configPath, IniParser.WriteWithSections(defaultConfig), cancellationToken);

        OwnershipHelper.TrySetOwnership(serverPath, _options.RunAsUid, _options.RunAsGid, _logger, recursive: true);
        OwnershipHelper.TrySetOwnership(backupPath, _options.RunAsUid, _options.RunAsGid, _logger, recursive: true);
        OwnershipHelper.TrySetOwnership(archivePath, _options.RunAsUid, _options.RunAsGid, _logger, recursive: true);

        _logger.LogInformation("Created server {ServerName} at {ServerPath}", request.Name, serverPath);

        return await GetServerAsync(request.Name, cancellationToken);
    }

    public async Task<ServerDetailDto> CloneServerAsync(
        string sourceName,
        string newName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("New server name is required.");
        }

        var sourcePath = GetServerPath(sourceName);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Server '{sourceName}' not found");
        }

        var targetPath = GetServerPath(newName);
        if (Directory.Exists(targetPath))
        {
            throw new InvalidOperationException($"Server '{newName}' already exists");
        }

        var isRunning = await _processManager.IsServerRunningAsync(sourceName, cancellationToken);
        if (isRunning)
        {
            throw new InvalidOperationException($"Cannot clone running server '{sourceName}'");
        }

        Directory.CreateDirectory(targetPath);
        await CopyDirectoryAsync(sourcePath, targetPath, cancellationToken, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "logs",
            "crash-reports"
        });

        var backupPath = GetBackupPath(newName);
        var archivePath = GetArchivePath(newName);
        Directory.CreateDirectory(backupPath);
        Directory.CreateDirectory(archivePath);

        var propertiesPath = GetPropertiesPath(newName);
        if (File.Exists(propertiesPath))
        {
            var properties = await GetServerPropertiesAsync(newName, cancellationToken);
            var usedPorts = await GetUsedPortsAsync(excludeName: null, cancellationToken);
            var defaultPort = GetNextAvailablePort(usedPorts, 25565);
            properties["server-port"] = defaultPort.ToString();
            await UpdateServerPropertiesAsync(newName, properties, cancellationToken);
        }

        OwnershipHelper.TrySetOwnership(targetPath, _options.RunAsUid, _options.RunAsGid, _logger, recursive: true);
        OwnershipHelper.TrySetOwnership(backupPath, _options.RunAsUid, _options.RunAsGid, _logger, recursive: true);
        OwnershipHelper.TrySetOwnership(archivePath, _options.RunAsUid, _options.RunAsGid, _logger, recursive: true);

        _logger.LogInformation("Cloned server {SourceServer} to {TargetServer}", sourceName, newName);
        return await GetServerAsync(newName, cancellationToken);
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

    public async Task<List<ServerDetailDto>> ListServersAsync(CancellationToken cancellationToken)
    {
        var serversDir = Path.Combine(_options.BaseDirectory, _options.ServersPathSegment);
        if (!Directory.Exists(serversDir))
        {
            return new List<ServerDetailDto>();
        }

        var serverNames = Directory.GetDirectories(serversDir)
            .Select(dir => Path.GetFileName(dir))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var serverDetails = new List<ServerDetailDto>();
        foreach (var name in serverNames)
        {
            try
            {
                var detail = await GetServerAsync(name!, cancellationToken);
                serverDetails.Add(detail);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get details for server {ServerName}", name);
            }
        }

        return serverDetails;
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

        ClearStopRequested(name);

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

        await EnsureEulaAcceptedAsync(serverPath, cancellationToken);
        await EnsureExecutableAvailableAsync("screen", "-v", "GNU screen", cancellationToken);
        await EnsureExecutableAvailableAsync(javaBinary, "-version", "Java runtime", cancellationToken);

        // Check if using modern Forge @argfile syntax (e.g., "@user_jvm_args.txt @libraries/.../unix_args.txt")
        var isArgFileSyntax = jarFile.TrimStart().StartsWith("@");

        if (isArgFileSyntax)
        {
            // Validate that each referenced arg file exists
            var argFiles = jarFile.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var argFile in argFiles)
            {
                var filePath = Path.Combine(serverPath, argFile.TrimStart('@'));
                if (!File.Exists(filePath))
                {
                    throw new InvalidOperationException($"Forge argument file not found: {filePath}");
                }
            }
            _logger.LogInformation("Using Forge @argfile syntax for {ServerName}: {ArgFiles}", name, jarFile);
        }
        else
        {
            var resolvedJarPath = ResolveJarPath(serverPath, jarFile);
            _logger.LogInformation("Resolved JAR path for {ServerName}: {JarPath}", name, resolvedJarPath);
            if (!File.Exists(resolvedJarPath))
            {
                throw new InvalidOperationException($"Configured JAR file not found: {resolvedJarPath}");
            }
        }

        var logDir = Path.Combine(serverPath, "logs");
        Directory.CreateDirectory(logDir);

        // Change ownership of logs directory to minecraft user so it can write logs
        var uid = _options.RunAsUid;
        var gid = _options.RunAsGid;
        await OwnershipHelper.ChangeOwnershipAsync(logDir, uid, gid, _logger, cancellationToken);

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

        // Modern Forge uses @argfile syntax instead of -jar
        if (isArgFileSyntax)
        {
            // Add each @argfile reference
            javaArgs.AddRange(jarFile.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        else
        {
            javaArgs.Add("-jar");
            javaArgs.Add(ResolveJarPath(serverPath, jarFile));
        }

        if (!string.IsNullOrEmpty(config.Java.JarArgs))
        {
            javaArgs.AddRange(config.Java.JarArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (!config.Minecraft.Unconventional)
        {
            javaArgs.Add("nogui");
        }

        var startTime = DateTimeOffset.UtcNow;
        var startupStamp = $"[{startTime:O}] Launching {name}";
        var javaCommand = string.Join(" ", javaArgs.Select(EscapeBashArgument));
        var startupLogArg = EscapeBashArgument(startupLogPath);
        var escapedServerPath = EscapeBashArgument(serverPath);
        var shellCommand = $"cd {escapedServerPath} && echo {EscapeBashArgument(startupStamp)} >> {startupLogArg}; exec {javaCommand} >> {startupLogArg} 2>&1";

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
        ClearRestartRequired(name);
        _logger.LogInformation("Started server {ServerName}", name);
    }

    public async Task StopServerAsync(string name, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var isRunning = await _processManager.IsServerRunningAsync(name, cancellationToken);
        if (!isRunning)
        {
            throw new InvalidOperationException($"Server '{name}' is not running");
        }

        await MarkStopRequestedAsync(name);

        var uid = _options.RunAsUid;
        var gid = _options.RunAsGid;

        // Send stop command
        await _processManager.SendCommandAsync(name, "stop", uid, gid, cancellationToken);

        // Wait for process to stop
        var timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 300);
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
        await StopServerAsync(name, 300, cancellationToken);
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

        await MarkStopRequestedAsync(name);
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
        var existingProperties = await GetServerPropertiesAsync(name, cancellationToken);
        var existingWorld = GetActiveWorldName(existingProperties);
        var updatedWorld = GetActiveWorldName(properties);
        var worldChanged = !string.Equals(existingWorld, updatedWorld, StringComparison.OrdinalIgnoreCase);

        if (properties.TryGetValue("server-port", out var portValue))
        {
            if (!int.TryParse(portValue, out var port) || port < 1 || port > 65535)
            {
                throw new InvalidOperationException("Server port must be a number between 1 and 65535.");
            }

            var usedPorts = await GetUsedPortsAsync(name, cancellationToken);
            if (usedPorts.Contains(port))
            {
                throw new InvalidOperationException($"Port {port} is already in use by another server.");
            }
        }

        var propertiesPath = GetPropertiesPath(name);
        var content = IniParser.WriteSimple(properties);
        await File.WriteAllTextAsync(propertiesPath, content, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(propertiesPath, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);

        if (worldChanged)
        {
            await MarkRestartRequiredAsync(name);
        }

        _logger.LogInformation("Updated server.properties for {ServerName}", name);
    }

    public async Task<ServerConfigDto> GetServerConfigAsync(string name, CancellationToken cancellationToken)
    {
        var configPath = GetConfigPath(name);
        if (!File.Exists(configPath))
        {
            // Return defaults
            return new ServerConfigDto(
                new JavaConfigDto("", 4096, 4096, null, null, null),
                new MinecraftConfigDto(null, false, false),
                new OnRebootConfigDto(false),
                new AutoRestartConfigDto(false, 3, 300, 30, true, true)
            );
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken);
        var sections = IniParser.ParseWithSections(content);

        var javaSection = sections.TryGetValue("java", out var javaSectionRaw)
            ? javaSectionRaw
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var minecraftSection = sections.TryGetValue("minecraft", out var minecraftSectionRaw)
            ? minecraftSectionRaw
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var onrebootSection = sections.TryGetValue("onreboot", out var onrebootSectionRaw)
            ? onrebootSectionRaw
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var autorestartSection = sections.TryGetValue("autorestart", out var autorestartSectionRaw)
            ? autorestartSectionRaw
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var java = new JavaConfigDto(
            javaSection.GetValueOrDefault("java_binary", ""),
            int.TryParse(javaSection.GetValueOrDefault("java_xmx", "4096"), out var xmx) ? xmx : 4096,
            int.TryParse(javaSection.GetValueOrDefault("java_xms", "4096"), out var xms) ? xms : 4096,
            GetOptionalValue(javaSection, "java_tweaks"),
            GetOptionalValue(javaSection, "jarfile"),
            GetOptionalValue(javaSection, "jar_args")
        );

        var minecraft = new MinecraftConfigDto(
            GetOptionalValue(minecraftSection, "profile"),
            bool.TryParse(minecraftSection.GetValueOrDefault("unconventional", "false"), out var unconventional) && unconventional,
            bool.TryParse(minecraftSection.GetValueOrDefault("lan_broadcast", "false"), out var lanBroadcast) && lanBroadcast
        );

        var onreboot = new OnRebootConfigDto(
            bool.TryParse(onrebootSection.GetValueOrDefault("start", "false"), out var start) && start
        );

        var autorestart = new AutoRestartConfigDto(
            bool.TryParse(autorestartSection.GetValueOrDefault("enabled", "false"), out var arEnabled) && arEnabled,
            int.TryParse(autorestartSection.GetValueOrDefault("max_attempts", "3"), out var maxAttempts) ? maxAttempts : 3,
            int.TryParse(autorestartSection.GetValueOrDefault("cooldown_seconds", "300"), out var cooldown) ? cooldown : 300,
            int.TryParse(autorestartSection.GetValueOrDefault("attempt_reset_minutes", "30"), out var resetMins) ? resetMins : 30,
            bool.TryParse(autorestartSection.GetValueOrDefault("notify_on_crash", "true"), out var notifyCrash) && notifyCrash,
            bool.TryParse(autorestartSection.GetValueOrDefault("notify_on_restart", "true"), out var notifyRestart) && notifyRestart
        );

        return new ServerConfigDto(java, minecraft, onreboot, autorestart);
    }

    public async Task UpdateServerConfigAsync(string name, ServerConfigDto config, CancellationToken cancellationToken)
    {
        var existing = await GetServerConfigAsync(name, cancellationToken);
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
                ["unconventional"] = config.Minecraft.Unconventional.ToString().ToLower(),
                ["lan_broadcast"] = config.Minecraft.LanBroadcast.ToString().ToLower()
            },
            ["onreboot"] = new()
            {
                ["start"] = config.OnReboot.Start.ToString().ToLower()
            },
            ["autorestart"] = new()
            {
                ["enabled"] = config.AutoRestart.Enabled.ToString().ToLower(),
                ["max_attempts"] = config.AutoRestart.MaxAttempts.ToString(),
                ["cooldown_seconds"] = config.AutoRestart.CooldownSeconds.ToString(),
                ["attempt_reset_minutes"] = config.AutoRestart.AttemptResetMinutes.ToString(),
                ["notify_on_crash"] = config.AutoRestart.NotifyOnCrash.ToString().ToLower(),
                ["notify_on_restart"] = config.AutoRestart.NotifyOnRestart.ToString().ToLower()
            }
        };

        var content = IniParser.WriteWithSections(sections);
        var configPath = GetConfigPath(name);
        await File.WriteAllTextAsync(configPath, content, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(configPath, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);

        var jarChanged = !string.Equals(existing.Java.JarFile ?? string.Empty, config.Java.JarFile ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var profileChanged = !string.Equals(existing.Minecraft.Profile ?? string.Empty, config.Minecraft.Profile ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        if (jarChanged || profileChanged)
        {
            await MarkRestartRequiredAsync(name);
        }

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

        // Change ownership so minecraft user can read the eula.txt file
        await OwnershipHelper.ChangeOwnershipAsync(eulaPath, _options.RunAsUid, _options.RunAsGid, _logger, cancellationToken);

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

    private static string? GetOptionalValue(IReadOnlyDictionary<string, string> section, string key)
    {
        return section.TryGetValue(key, out var value) ? value : null;
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

    private static async Task EnsureEulaAcceptedAsync(string serverPath, CancellationToken cancellationToken)
    {
        var eulaPath = Path.Combine(serverPath, "eula.txt");
        if (!File.Exists(eulaPath))
        {
            throw new InvalidOperationException("EULA has not been accepted yet. Call /servers/{name}/eula first.");
        }

        var lines = await File.ReadAllLinesAsync(eulaPath, cancellationToken);
        var eulaLine = lines.FirstOrDefault(line => line.TrimStart().StartsWith("eula=", StringComparison.OrdinalIgnoreCase));
        if (eulaLine == null || !eulaLine.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("EULA has not been accepted yet. Call /servers/{name}/eula first.");
        }
    }

    private static async Task<bool> IsEulaAcceptedAsync(string serverPath, CancellationToken cancellationToken)
    {
        var eulaPath = Path.Combine(serverPath, "eula.txt");
        if (!File.Exists(eulaPath))
        {
            return false;
        }

        var lines = await File.ReadAllLinesAsync(eulaPath, cancellationToken);
        var eulaLine = lines.FirstOrDefault(line => line.TrimStart().StartsWith("eula=", StringComparison.OrdinalIgnoreCase));
        return eulaLine != null && eulaLine.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task MarkRestartRequiredAsync(string name)
    {
        var flagPath = GetRestartFlagPath(name);
        try
        {
            await File.WriteAllTextAsync(flagPath, DateTimeOffset.UtcNow.ToString("O"));
            OwnershipHelper.TrySetOwnership(flagPath, _options.RunAsUid, _options.RunAsGid, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark restart required for {ServerName}", name);
        }
    }

    private void ClearRestartRequired(string name)
    {
        var flagPath = GetRestartFlagPath(name);
        if (File.Exists(flagPath))
        {
            try
            {
                File.Delete(flagPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear restart flag for {ServerName}", name);
            }
        }
    }

    private async Task MarkStopRequestedAsync(string name)
    {
        var flagPath = GetStopRequestedFlagPath(name);
        try
        {
            await File.WriteAllTextAsync(flagPath, DateTimeOffset.UtcNow.ToString("O"));
            OwnershipHelper.TrySetOwnership(flagPath, _options.RunAsUid, _options.RunAsGid, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark stop requested for {ServerName}", name);
        }
    }

    private void ClearStopRequested(string name)
    {
        var flagPath = GetStopRequestedFlagPath(name);
        if (File.Exists(flagPath))
        {
            try
            {
                File.Delete(flagPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear stop requested flag for {ServerName}", name);
            }
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
        var latestLog = File.Exists(logPath) ? await ReadLogTailAsync(logPath, 20, cancellationToken) : "No latest.log output found.";
        var startupLog = File.Exists(startupLogPath) ? await ReadLogTailAsync(startupLogPath, 40, cancellationToken) : "No startup.log output found.";
        throw new InvalidOperationException($"Server '{name}' did not start. {latestLog} {startupLog}");
    }

    private static async Task<string> ReadLogTailAsync(string logPath, int maxLines, CancellationToken cancellationToken)
    {
        try
        {
            var allLines = await File.ReadAllLinesAsync(logPath, cancellationToken);
            var lines = allLines.TakeLast(maxLines).ToList();
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

    private async Task<HashSet<int>> GetUsedPortsAsync(string? excludeName, CancellationToken cancellationToken)
    {
        var usedPorts = new HashSet<int>();
        var serversPath = Path.Combine(_options.BaseDirectory, _options.ServersPathSegment);
        if (!Directory.Exists(serversPath))
        {
            return usedPorts;
        }

        foreach (var dir in Directory.EnumerateDirectories(serversPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serverName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(serverName))
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(excludeName) &&
                serverName.Equals(excludeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propertiesPath = Path.Combine(dir, "server.properties");
            if (!File.Exists(propertiesPath))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(propertiesPath, cancellationToken);
            var props = IniParser.ParseSimple(content);
            if (props.TryGetValue("server-port", out var portValue) &&
                int.TryParse(portValue, out var port))
            {
                usedPorts.Add(port);
            }
        }

        return usedPorts;
    }

    private static int GetNextAvailablePort(HashSet<int> usedPorts, int startPort)
    {
        var port = startPort;
        while (usedPorts.Contains(port))
        {
            port++;
            if (port > 65535)
            {
                throw new InvalidOperationException("No available ports were found.");
            }
        }

        return port;
    }

    private static string GetActiveWorldName(Dictionary<string, string> properties)
    {
        if (properties.TryGetValue("level-name", out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return "world";
    }

    private static async Task CopyDirectoryAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken,
        HashSet<string> excludedDirectories)
    {
        foreach (var directory in Directory.GetDirectories(sourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryName = Path.GetFileName(directory);
            if (directoryName != null && excludedDirectories.Contains(directoryName))
            {
                continue;
            }

            var targetDirectory = Path.Combine(targetPath, directoryName ?? string.Empty);
            Directory.CreateDirectory(targetDirectory);
            await CopyDirectoryAsync(directory, targetDirectory, cancellationToken, excludedDirectories);
        }

        foreach (var file in Directory.GetFiles(sourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            var targetFile = Path.Combine(targetPath, fileName);
            await using var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            await using var targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
        }
    }
}
