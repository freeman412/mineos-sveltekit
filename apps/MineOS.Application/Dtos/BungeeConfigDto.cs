namespace MineOS.Application.Dtos;

// Models BungeeCord's config.yml. Only the fields the editor surfaces are
// mapped; anything else present in the file (extra listeners, fork-specific
// keys) is preserved on round-trip rather than dropped.
public record BungeeConfigDto(
    bool Exists,
    // top-level
    bool OnlineMode,
    bool IpForward,
    int PlayerLimit,
    int Timeout,
    int NetworkCompressionThreshold,
    bool ForgeSupport,
    bool LogCommands,
    bool LogPings,
    int ConnectionThrottle,
    int ConnectionThrottleLimit,
    // first listener (BungeeCord supports multiple but the editor exposes #0;
    // any additional listeners in config.yml are preserved on round-trip)
    string Host,
    string Motd,
    int MaxPlayers,
    bool QueryEnabled,
    int QueryPort,
    bool PingPassthrough,
    bool ForceDefaultServer,
    string TabList,
    bool ProxyProtocol,
    List<string> Priorities,
    Dictionary<string, string> ForcedHosts, // BungeeCord supports a single server per host (not a list).
    // backends
    Dictionary<string, BungeeBackendDto> Servers);

public record BungeeBackendDto(
    string Address,
    string Motd,
    bool Restricted);
