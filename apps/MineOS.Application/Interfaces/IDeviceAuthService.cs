using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IDeviceAuthService
{
    Task<DeviceCodeResponse> InitiateAsync(CancellationToken ct = default);
    Task<LinkStatus> GetStatusAsync(CancellationToken ct = default);
    Task UnlinkAsync(CancellationToken ct = default);
    Task<LinkedAccountInfo?> GetLinkedAccountAsync(CancellationToken ct = default);
}
