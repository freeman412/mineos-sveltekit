using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;
using MineOS.Domain.ValueObjects;

namespace MineOS.Infrastructure.Services;

/// <summary>
/// Answers "can the outside world reach this port?" by asking Docker about the
/// container MineOS is running in, over the socket docker-compose already mounts.
///
/// .NET speaks Unix sockets natively, so this needs no Docker client library.
///
/// Every failure path returns <see cref="ExposureVerdict.Unknown"/>. This verdict
/// is a security control for backends that cannot verify forwarded players, and a
/// check that reports "not exposed" because it could not look is worse than no
/// check at all.
/// </summary>
public sealed class DockerPortInspector : IContainerPortInspector
{
    private const string SocketPath = "/var/run/docker.sock";

    private readonly ILogger<DockerPortInspector> _logger;

    public DockerPortInspector(ILogger<DockerPortInspector> logger)
    {
        _logger = logger;
    }

    public async Task<(ExposureVerdict Verdict, string? Detail)> IsPortPublishedAsync(
        int port, CancellationToken cancellationToken)
    {
        if (!File.Exists(SocketPath))
        {
            return (ExposureVerdict.Unknown,
                "MineOS cannot see the Docker socket, so it cannot tell whether this port is reachable from outside. " +
                "Check your firewall or port forwarding by hand.");
        }

        var containerId = ResolveContainerId();
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return (ExposureVerdict.Unknown, "Could not determine which container MineOS is running in.");
        }

        try
        {
            using var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            client.Timeout = TimeSpan.FromSeconds(5);

            using var response = await client.GetAsync(
                $"/containers/{containerId}/json", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (ExposureVerdict.Unknown,
                    $"Docker returned {(int)response.StatusCode} when asked about this container.");
            }

            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            return Evaluate(doc.RootElement, port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Docker port inspection failed");
            return (ExposureVerdict.Unknown, "Could not query Docker about published ports.");
        }
    }

    /// <summary>
    /// Reads the exposure answer out of a container inspect payload. Separated from
    /// the transport so the interesting cases are testable without a Docker daemon.
    /// </summary>
    internal static (ExposureVerdict Verdict, string? Detail) Evaluate(JsonElement container, int port)
    {
        if (!container.TryGetProperty("HostConfig", out var hostConfig))
        {
            return (ExposureVerdict.Unknown, "Docker's response did not include host configuration.");
        }

        // Host networking bypasses port publishing entirely: every listening port
        // is on the host's own interfaces, MC_PORT_RANGE notwithstanding.
        if (hostConfig.TryGetProperty("NetworkMode", out var networkMode) &&
            string.Equals(networkMode.GetString(), "host", StringComparison.OrdinalIgnoreCase))
        {
            return (ExposureVerdict.Exposed,
                "MineOS is running with host networking, so every server port is reachable on the host's network.");
        }

        if (!hostConfig.TryGetProperty("PortBindings", out var bindings) ||
            bindings.ValueKind != JsonValueKind.Object)
        {
            return (ExposureVerdict.Unknown, "Docker's response did not include port bindings.");
        }

        foreach (var binding in bindings.EnumerateObject())
        {
            // Keys look like "25565/tcp"; a null value means the key exists but
            // publishes nothing, which is not an exposure.
            var slash = binding.Name.IndexOf('/');
            var portPart = slash > 0 ? binding.Name[..slash] : binding.Name;
            if (!int.TryParse(portPart, out var boundPort) || boundPort != port)
            {
                continue;
            }
            if (binding.Value.ValueKind == JsonValueKind.Array && binding.Value.GetArrayLength() > 0)
            {
                return (ExposureVerdict.Exposed,
                    $"Port {port} is published to the host, so this server can be reached directly.");
            }
        }

        return (ExposureVerdict.NotExposed,
            $"Port {port} is not published to the host, so this server is reachable only through the proxy.");
    }

    private static string? ResolveContainerId()
    {
        // Docker sets the container's hostname to its short id unless overridden.
        var hostname = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrWhiteSpace(hostname))
        {
            return hostname;
        }

        try
        {
            return File.Exists("/etc/hostname")
                ? File.ReadAllText("/etc/hostname").Trim()
                : Environment.MachineName;
        }
        catch
        {
            return Environment.MachineName;
        }
    }
}
