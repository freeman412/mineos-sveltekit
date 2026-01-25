using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Background;

public sealed class LanBroadcastService : BackgroundService
{
    private const string MulticastAddress = "224.0.2.60";
    private const int MulticastPort = 4445;
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(1.5);

    private readonly HostOptions _hostOptions;
    private readonly IProcessManager _processManager;
    private readonly ILogger<LanBroadcastService> _logger;
    private bool _dockerWarningLogged;

    public LanBroadcastService(
        IOptions<HostOptions> hostOptions,
        IProcessManager processManager,
        ILogger<LanBroadcastService> logger)
    {
        _hostOptions = hostOptions.Value;
        _processManager = processManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "LAN broadcast cycle failed");
            }

            try
            {
                await Task.Delay(BroadcastInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task BroadcastOnceAsync(CancellationToken cancellationToken)
    {
        LogDockerMulticastWarningOnce();

        var serversPath = Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment);
        if (!Directory.Exists(serversPath))
        {
            return;
        }

        var processes = _processManager.GetServerProcesses();

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        var endpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);

        foreach (var dir in Directory.EnumerateDirectories(serversPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var serverName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(serverName))
            {
                continue;
            }

            if (!processes.ContainsKey(serverName))
            {
                continue;
            }

            if (!IsLanBroadcastEnabled(dir))
            {
                continue;
            }

            if (!TryGetLanAnnouncement(dir, serverName, out var message))
            {
                continue;
            }

            var payload = Encoding.UTF8.GetBytes(message);
            await client.SendAsync(payload, payload.Length, endpoint);
        }
    }

    private void LogDockerMulticastWarningOnce()
    {
        if (_dockerWarningLogged)
        {
            return;
        }

        var inDocker = File.Exists("/.dockerenv");
        if (!inDocker && File.Exists("/proc/1/cgroup"))
        {
            inDocker = File.ReadAllText("/proc/1/cgroup").Contains("docker", StringComparison.OrdinalIgnoreCase);
        }

        if (inDocker)
        {
            _logger.LogWarning("LAN broadcast is enabled but the service is running in a container. Multicast discovery may not reach your LAN unless you use host networking or multicast-capable networking.");
            _dockerWarningLogged = true;
        }
    }

    private static bool IsLanBroadcastEnabled(string serverPath)
    {
        var configPath = Path.Combine(serverPath, "server.config");
        if (!File.Exists(configPath))
        {
            return false;
        }

        var content = File.ReadAllText(configPath);
        var sections = IniParser.ParseWithSections(content);
        if (!sections.TryGetValue("minecraft", out var minecraftSection))
        {
            return false;
        }

        if (!minecraftSection.TryGetValue("lan_broadcast", out var value))
        {
            return false;
        }

        return bool.TryParse(value, out var enabled) && enabled;
    }

    private static bool TryGetLanAnnouncement(string serverPath, string serverName, out string message)
    {
        message = string.Empty;
        var propertiesPath = Path.Combine(serverPath, "server.properties");
        if (!File.Exists(propertiesPath))
        {
            return false;
        }

        var content = File.ReadAllText(propertiesPath);
        var properties = IniParser.ParseSimple(content);

        var motd = properties.TryGetValue("motd", out var motdValue) && !string.IsNullOrWhiteSpace(motdValue)
            ? motdValue
            : serverName;

        var port = 25565;
        if (properties.TryGetValue("server-port", out var portValue) &&
            int.TryParse(portValue, out var parsedPort) &&
            parsedPort is >= 1 and <= 65535)
        {
            port = parsedPort;
        }

        message = $"[MOTD]{motd}[/MOTD][AD]{port}[/AD]";
        return true;
    }
}
