using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Persistence;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Services;

public sealed class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<UserService> _logger;

    public UserService(AppDbContext db, IPasswordHasher passwordHasher, ILogger<UserService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken);

        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new ArgumentException("Username is required");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Password is required");
        }

        var normalized = request.Username.Trim();
        var existing = await _db.Users.AnyAsync(
            u => u.Username.ToLower() == normalized.ToLower(),
            cancellationToken);

        if (existing)
        {
            throw new InvalidOperationException("Username already exists");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = normalized,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = string.IsNullOrWhiteSpace(request.Role) ? "user" : request.Role.Trim().ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created user {Username}", user.Username);
        return ToDto(user);
    }

    public async Task<UserDto> UpdateUserAsync(Guid id, UpdateUserRequestDto request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
        {
            throw new ArgumentException("User not found");
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = _passwordHasher.Hash(request.Password);
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            user.Role = request.Role.Trim().ToLowerInvariant();
        }

        if (request.IsActive.HasValue)
        {
            user.IsActive = request.IsActive.Value;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated user {Username}", user.Username);
        return ToDto(user);
    }

    private static UserDto ToDto(User user)
    {
        return new UserDto(
            user.Id,
            user.Username,
            user.Role,
            user.IsActive,
            user.CreatedAt);
    }
}
