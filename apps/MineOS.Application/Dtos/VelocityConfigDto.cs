namespace MineOS.Application.Dtos;

public record VelocityConfigDto(
    bool Exists,
    string Bind,
    string Motd,
    int ShowMaxPlayers,
    bool OnlineMode,
    bool ForceKeyAuthentication,
    bool PreventClientProxyConnections,
    string PlayerInfoForwardingMode,
    string ForwardingSecretFile,
    bool AnnounceForge,
    bool KickExistingPlayers,
    string PingPassthrough,
    bool EnablePlayerAddressLogging,
    Dictionary<string, string> Servers,
    List<string> Try);
