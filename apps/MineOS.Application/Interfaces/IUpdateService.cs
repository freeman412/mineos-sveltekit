using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

/// <summary>
/// Server software update detection and apply (issue #83). Detection compares a
/// server's installed jar/binary against the profile catalog; apply performs a
/// backup-then-swap of the server's software. Modded loaders (Forge, NeoForge,
/// Fabric, Quilt) are deliberately unsupported — loader swaps can strand mods.
/// </summary>
public interface IUpdateService
{
    Task<ServerUpdateStatusDto> GetUpdateStatusAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the per-server notification mode: "notify" (badge on updates),
    /// "off" (never notify), or "ignore-current" (dismiss the pending update;
    /// the badge returns when something even newer ships).
    /// </summary>
    Task SetUpdateModeAsync(string name, string mode, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the target profile and swaps it into the server. Refuses while
    /// the server is running (InvalidOperationException) or when the profile
    /// does not belong to the server's family (ArgumentException).
    /// </summary>
    Task<ApplyUpdateResultDto> ApplyUpdateAsync(string name, string profileId, CancellationToken cancellationToken);
}
