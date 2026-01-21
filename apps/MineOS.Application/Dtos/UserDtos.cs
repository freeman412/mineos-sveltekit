namespace MineOS.Application.Dtos;

public record UserDto(
    int Id,
    string Username,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string? MinecraftUsername,
    string? MinecraftUuid,
    IReadOnlyList<ServerAccessDto> ServerAccesses);

public record CreateUserRequestDto(
    string Username,
    string Password,
    string Role,
    string? MinecraftUsername,
    IReadOnlyList<ServerAccessRequestDto>? ServerAccesses);

public record UpdateUserRequestDto(
    string? Password,
    string? Role,
    bool? IsActive,
    string? MinecraftUsername,
    IReadOnlyList<ServerAccessRequestDto>? ServerAccesses);

public record UpdateSelfRequestDto(string? Username, string? Password);

public record ServerAccessDto(
    string ServerName,
    bool CanView,
    bool CanControl,
    bool CanConsole);

public record ServerAccessRequestDto(
    string ServerName,
    bool CanView,
    bool CanControl,
    bool CanConsole);
