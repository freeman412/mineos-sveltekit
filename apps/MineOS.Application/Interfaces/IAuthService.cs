using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResultDto?> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken);
}
