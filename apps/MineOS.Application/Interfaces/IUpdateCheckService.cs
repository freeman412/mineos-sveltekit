namespace MineOS.Application.Interfaces;

/// <summary>
/// The latest known update for a server's installed server software.
/// </summary>
public sealed record ServerUpdateInfo(
    string ServerName,
    string Component,        // e.g. "paper", "fabric loader", "neoforge", "forge", "quilt loader"
    string InstalledVersion,
    string LatestVersion,
    DateTimeOffset CheckedAt);

/// <summary>
/// Read side of the periodic server-software update check. Detection is
/// conservative: a server with an unrecognizable installed version simply has
/// no update info (never a false positive).
/// </summary>
public interface IUpdateCheckService
{
    ServerUpdateInfo? GetUpdateInfo(string serverName);
}
