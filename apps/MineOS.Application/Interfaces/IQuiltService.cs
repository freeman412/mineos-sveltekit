using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IQuiltService
{
    Task<IReadOnlyList<QuiltGameVersionDto>> GetGameVersionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<QuiltLoaderVersionDto>> GetLoaderVersionsAsync(CancellationToken cancellationToken);
    Task<QuiltInstallResultDto> InstallQuiltAsync(
        string minecraftVersion, string loaderVersion, string serverName,
        CancellationToken cancellationToken);
    Task<QuiltInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken);
    IReadOnlyList<QuiltInstallStatusDto> GetActiveInstalls();
}
