using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IModDependencyService
{
    /// <summary>
    /// Works out everything installing a Modrinth version would pull in, without
    /// writing anything: hard dependencies (including dependencies of
    /// dependencies), optional ones to offer, and which are already present.
    /// </summary>
    Task<ModInstallPlanDto> PlanInstallAsync(
        string serverName, string versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Installs a mod together with its hard dependencies, plus any optional
    /// dependencies named in <paramref name="approvedOptionalProjectIds"/>.
    ///
    /// Refuses when a hard dependency cannot be resolved for this server's loader
    /// and Minecraft version. Installing part of the set would leave a server that
    /// does not start — for Fabric, a missing required dependency aborts boot
    /// outright — so nothing is written in that case.
    /// </summary>
    Task<ModInstallResultDto> InstallWithDependenciesAsync(
        string serverName,
        string versionId,
        IReadOnlyList<string> approvedOptionalProjectIds,
        CancellationToken cancellationToken);
}
