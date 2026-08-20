namespace MineOS.Domain.ValueObjects;

/// <summary>
/// What a backend server's forwarding configuration currently amounts to.
/// Always derived from files on disk at read time — never stored. A persisted
/// "secured" flag goes stale the moment someone edits server.properties by hand,
/// and a stale security badge is worse than no badge at all.
/// </summary>
public enum ProxyForwardingStatus
{
    /// <summary>Not listed as a backend by any proxy. Nothing to report.</summary>
    NotABackend,

    /// <summary>Verified forwarding, secrets agree, online-mode off. Correct.</summary>
    Secured,

    /// <summary>Enrolled but wrong. See <see cref="ForwardingAssessment.IsSpoofable"/>
    /// for whether "wrong" currently means "anyone can join as anyone".</summary>
    Misconfigured,

    /// <summary>Not secured yet, but this backend can be: one click, or one mod.</summary>
    Securable,

    /// <summary>No verified-forwarding path exists for this combination.
    /// Network isolation is the only control available.</summary>
    Unverifiable
}

/// <summary>What kind of forwarding the proxy in front of this server performs.</summary>
public enum ProxyForwardingKind
{
    /// <summary>No proxy claims this server.</summary>
    None,

    /// <summary>Velocity with player-info-forwarding-mode = "modern": the forwarded
    /// player data is signed with the shared secret and the backend verifies it.</summary>
    VelocityModern,

    /// <summary>Velocity in none/legacy/bungeeguard mode — nothing the backend can verify.</summary>
    VelocityUnverified,

    /// <summary>BungeeCord. ip_forward carries no signature at all, so a direct
    /// connection to the backend can claim any identity.</summary>
    BungeeCord
}

/// <summary>Whether the backend's server software can verify forwarded player data.</summary>
public enum LoaderTier
{
    /// <summary>Paper, Purpur, Folia — native modern forwarding via paper-global.yml.</summary>
    Native,

    /// <summary>Fabric/Quilt — possible, but only with FabricProxy-Lite installed.</summary>
    ModRequired,

    /// <summary>Forge, NeoForge, vanilla, Spigot, CraftBukkit — no verified path.</summary>
    Unsupported
}

/// <summary>Whether the backend's port can actually be reached from outside the host.</summary>
public enum ExposureVerdict
{
    /// <summary>Could not be determined. Never treat this as safe.</summary>
    Unknown,

    /// <summary>Positively determined that the port is not published.</summary>
    NotExposed,

    /// <summary>The port is published to the host, or the container uses host networking.</summary>
    Exposed
}

/// <summary>
/// The facts a status decision is made from. Gathering these touches the
/// filesystem; deciding from them does not, which is what keeps
/// <see cref="ProxyForwardingRules"/> pure and cheap to test.
/// </summary>
public sealed record ForwardingFacts(
    bool IsBackend,
    ProxyForwardingKind ProxyKind,
    LoaderTier Tier,
    // paper-global.yml (or FabricProxy-Lite.toml) exists and has forwarding enabled.
    bool BackendForwardingConfigured,
    // The backend's configured secret equals the proxy's forwarding.secret.
    bool SecretMatches,
    // online-mode in the backend's server.properties.
    bool ServerOnlineMode);

/// <summary>Status plus the one bit that actually matters for safety.</summary>
public sealed record ForwardingAssessment(
    ProxyForwardingStatus Status,
    bool IsSpoofable);

public static class ProxyForwardingRules
{
    /// <summary>
    /// Decides a backend's forwarding status.
    ///
    /// The security-critical combination is online-mode=false *without* verified
    /// forwarding: the backend has stopped authenticating players itself and
    /// nothing has taken over that job, so any direct connection can claim any
    /// username and UUID. That is reported as <see cref="ForwardingAssessment.IsSpoofable"/>,
    /// separately from the status, because a server can be misconfigured while
    /// still being perfectly safe (online-mode left on behind a proxy is merely
    /// broken — players cannot join, but nobody can impersonate them either).
    /// </summary>
    public static ForwardingAssessment Resolve(ForwardingFacts facts)
    {
        if (!facts.IsBackend)
        {
            return new ForwardingAssessment(ProxyForwardingStatus.NotABackend, IsSpoofable: false);
        }

        var verified =
            facts.ProxyKind == ProxyForwardingKind.VelocityModern &&
            facts.Tier != LoaderTier.Unsupported &&
            facts.BackendForwardingConfigured &&
            facts.SecretMatches;

        // online-mode=false is what removes the backend's own authentication.
        // Without verified forwarding replacing it, the server is open.
        var spoofable = !facts.ServerOnlineMode && !verified;

        if (verified)
        {
            // Verified forwarding requires online-mode=false; Velocity performs the
            // Mojang handshake instead. Leaving it on is broken, not dangerous.
            return new ForwardingAssessment(
                facts.ServerOnlineMode ? ProxyForwardingStatus.Misconfigured : ProxyForwardingStatus.Secured,
                IsSpoofable: false);
        }

        // No verified path exists for this pairing, so securing it is not on offer.
        // Say so plainly rather than dangling a fix button that cannot work.
        if (facts.ProxyKind == ProxyForwardingKind.BungeeCord || facts.Tier == LoaderTier.Unsupported)
        {
            return new ForwardingAssessment(ProxyForwardingStatus.Unverifiable, spoofable);
        }

        return new ForwardingAssessment(
            spoofable ? ProxyForwardingStatus.Misconfigured : ProxyForwardingStatus.Securable,
            spoofable);
    }

    /// <summary>Maps a detected loader onto what it can do about forwarding.</summary>
    public static LoaderTier TierFor(string? loader) => (loader ?? string.Empty).ToLowerInvariant() switch
    {
        // Paper's forwarding implementation; Purpur and Folia inherit it.
        "paper" or "purpur" or "folia" => LoaderTier.Native,
        // Needs FabricProxy-Lite. Quilt runs Fabric mods.
        "fabric" or "quilt" => LoaderTier.ModRequired,
        // Spigot/CraftBukkit are deliberately here: modern forwarding is a Paper
        // feature, not a Bukkit one, so they have no verified path either.
        _ => LoaderTier.Unsupported
    };
}
