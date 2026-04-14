using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IFabricService
{
    Task<IReadOnlyList<FabricGameVersionDto>> GetGameVersionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FabricLoaderVersionDto>> GetLoaderVersionsAsync(CancellationToken cancellationToken);
    Task<FabricInstallResultDto> InstallFabricAsync(
        string minecraftVersion,
        string loaderVersion,
        string serverName,
        CancellationToken cancellationToken);
    Task<FabricInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken);
    IReadOnlyList<FabricInstallStatusDto> GetActiveInstalls();
}
