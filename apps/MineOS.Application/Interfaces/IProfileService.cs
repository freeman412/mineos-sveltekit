using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IProfileService
{
    Task<IEnumerable<ProfileInfoDto>> ListProfilesAsync(CancellationToken cancellationToken);
    Task<ProfileInfoDto?> GetProfileAsync(string id, CancellationToken cancellationToken);
    Task<string> DownloadProfileAsync(string id, CancellationToken cancellationToken);
    Task CopyProfileToServerAsync(string profileId, string serverName, CancellationToken cancellationToken);
    IAsyncEnumerable<ProfileDownloadProgressDto> StreamDownloadProgressAsync(string id, CancellationToken cancellationToken);
}
