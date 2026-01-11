namespace MineOS.Application.Dtos;

public record UserDto(
    Guid Id,
    string Username,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateUserRequestDto(string Username, string Password, string Role);

public record UpdateUserRequestDto(string? Password, string? Role, bool? IsActive);
