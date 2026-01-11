using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken cancellationToken);
    Task<UserDto> CreateUserAsync(CreateUserRequestDto request, CancellationToken cancellationToken);
    Task<UserDto> UpdateUserAsync(int id, UpdateUserRequestDto request, CancellationToken cancellationToken);
}
