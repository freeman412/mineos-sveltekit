namespace MineOS.Application.Dtos;

/// <summary>
/// A backend server's forwarding posture, derived fresh on every read.
///
/// Note what is absent: the forwarding secret. Whether the two sides agree is
/// reported as <see cref="SecretMatches"/> and the value itself never crosses
/// the API boundary — not in full, not redacted. The existing
/// <see cref="VelocityConfigDto"/> exposes only the secret *file name*, and this
/// keeps to that line.
/// </summary>
public record BackendForwardingDto(
    string ServerName,
    // Enum *names*, not numbers. System.Text.Json serializes enums as integers by
    // default, and the web client matches on names ('Secured', 'Exposed'). Sending
    // 3 where the UI expects "Securable" fails silently: the panel renders with an
    // empty headline rather than erroring, so nothing surfaces the mismatch.
    string Status,
    // True when the server currently accepts unauthenticated connections:
    // online-mode is off and nothing verifies the forwarded identity.
    bool IsSpoofable,
    string ProxyKind,
    string Tier,
    string? ProxyName,
    string? Loader,
    bool ServerOnlineMode,
    bool BackendForwardingConfigured,
    bool SecretMatches,
    // Only meaningful when no verified path exists — then isolation is the
    // only control and we report whether it actually holds.
    string Exposure,
    string? ExposureDetail,
    // What the caller can do about it, if anything: "secure", "install-mod", or null.
    string? RemediationAction);

/// <summary>One row of a proxy's backend roll-up.</summary>
public record ProxyBackendSummaryDto(
    string ProxyName,
    IReadOnlyList<BackendForwardingDto> Backends);
