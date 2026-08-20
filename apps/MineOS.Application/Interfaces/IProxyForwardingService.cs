using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IProxyForwardingService
{
    /// <summary>
    /// Derives the forwarding posture of a single server. Reads only — safe to
    /// call on every page load, and deliberately not cached, so a hand-edited
    /// server.properties is reflected immediately.
    /// </summary>
    Task<BackendForwardingDto> GetForwardingStatusAsync(string serverName, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the forwarding posture of every backend a proxy claims.
    /// Callers are responsible for filtering to servers the user may see.
    /// </summary>
    Task<ProxyBackendSummaryDto> GetProxyBackendsAsync(string proxyName, CancellationToken cancellationToken);

    /// <summary>
    /// Configures verified (modern) forwarding between a backend and the proxy
    /// that claims it: ensures the proxy's forwarding secret exists, writes the
    /// backend's half, and only then turns off the backend's own online-mode.
    ///
    /// That ordering is deliberate. A failure part-way leaves a server that still
    /// authenticates its own players — broken behind a proxy, but not open. The
    /// reverse order would leave it live and unauthenticated.
    ///
    /// Idempotent. Throws <see cref="InvalidOperationException"/> when no proxy
    /// claims the server, when more than one does, or when the backend's loader
    /// has no verified-forwarding path.
    /// </summary>
    Task<BackendForwardingDto> SecureBackendAsync(string serverName, CancellationToken cancellationToken);

    /// <summary>
    /// Installs the mod a Fabric backend needs before it can verify forwarded
    /// players (FabricProxy-Lite), picking a build that matches the server's
    /// Minecraft version.
    ///
    /// Idempotent. Throws <see cref="InvalidOperationException"/> when the server
    /// is not Fabric, or when no build matches — installing a mismatched build
    /// would leave a server that looks secured while verifying nothing.
    /// </summary>
    Task<BackendForwardingDto> InstallForwardingModAsync(string serverName, CancellationToken cancellationToken);
}
