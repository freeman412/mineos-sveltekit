using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface INeoForgeService
{
    Task<IReadOnlyList<NeoForgeVersionDto>> GetVersionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NeoForgeVersionDto>> GetVersionsForMinecraftAsync(string minecraftVersion, CancellationToken cancellationToken);
    Task<NeoForgeInstallResultDto> InstallNeoForgeAsync(string minecraftVersion, string neoForgeVersion, string serverName, CancellationToken cancellationToken);
    Task<NeoForgeInstallStatusDto?> GetInstallStatusAsync(string installId, CancellationToken cancellationToken);
    IReadOnlyList<NeoForgeInstallStatusDto> GetActiveInstalls();
}
