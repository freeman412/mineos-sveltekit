using System.Linq;
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
    private readonly IMojangApiService _mojangApiService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        AppDbContext db,
        IPasswordHasher passwordHasher,
        IMojangApiService mojangApiService,
        ILogger<UserService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _mojangApiService = mojangApiService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(u => u.Id).ToList();
        var accessList = await _db.ServerAccesses
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .ToListAsync(cancellationToken);

        var accessByUserId = accessList
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return users.Select(user =>
            ToDto(user, accessByUserId.TryGetValue(user.Id, out var access) ? access : Array.Empty<ServerAccess>()))
            .ToList();
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

        var minecraftLink = await ResolveMinecraftProfileAsync(request.MinecraftUsername, cancellationToken);

        var user = new User
        {
            Username = normalized,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = string.IsNullOrWhiteSpace(request.Role) ? "user" : request.Role.Trim().ToLowerInvariant(),
            MinecraftUsername = minecraftLink.Username,
            MinecraftUuid = minecraftLink.Uuid,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.ServerAccesses != null)
        {
            await SyncServerAccessesAsync(user.Id, request.ServerAccesses, cancellationToken);
        }

        _logger.LogInformation("Created user {Username}", user.Username);
        var accesses = await LoadServerAccessesAsync(user.Id, cancellationToken);
        return ToDto(user, accesses);
    }

    public async Task<UserDto> UpdateUserAsync(int id, UpdateUserRequestDto request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
        {
            throw new ArgumentException("User not found");
        }

        if (request.MinecraftUsername != null)
        {
            var minecraftLink = await ResolveMinecraftProfileAsync(request.MinecraftUsername, cancellationToken);
            user.MinecraftUsername = minecraftLink.Username;
            user.MinecraftUuid = minecraftLink.Uuid;
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

        if (request.ServerAccesses != null)
        {
            await SyncServerAccessesAsync(user.Id, request.ServerAccesses, cancellationToken);
        }

        _logger.LogInformation("Updated user {Username}", user.Username);
        var accesses = await LoadServerAccessesAsync(user.Id, cancellationToken);
        return ToDto(user, accesses);
    }

    public async Task<UserDto> UpdateSelfAsync(int id, UpdateSelfRequestDto request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
        {
            throw new ArgumentException("User not found");
        }

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var normalized = request.Username.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Username is required");
            }

            if (!string.Equals(user.Username, normalized, StringComparison.OrdinalIgnoreCase))
            {
                var exists = await _db.Users.AnyAsync(
                    u => u.Username.ToLower() == normalized.ToLower() && u.Id != id,
                    cancellationToken);

                if (exists)
                {
                    throw new InvalidOperationException("Username already exists");
                }

                user.Username = normalized;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = _passwordHasher.Hash(request.Password);
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated profile for user {Username}", user.Username);
        var accesses = await LoadServerAccessesAsync(user.Id, cancellationToken);
        return ToDto(user, accesses);
    }

    public async Task DeleteUserAsync(int id, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
        {
            throw new ArgumentException("User not found");
        }

        // Prevent deleting the last admin
        if (user.Role == "admin")
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "admin" && u.IsActive, cancellationToken);
            if (adminCount <= 1)
            {
                throw new InvalidOperationException("Cannot delete the last admin user");
            }
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Deleted user {Username}", user.Username);
    }

    private static UserDto ToDto(User user, IReadOnlyList<ServerAccess> accesses)
    {
        var accessDtos = accesses
            .OrderBy(x => x.ServerName)
            .Select(access => new ServerAccessDto(access.ServerName, access.CanView, access.CanControl, access.CanConsole))
            .ToList();

        return new UserDto(
            user.Id,
            user.Username,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.MinecraftUsername,
            user.MinecraftUuid,
            accessDtos);
    }

    private async Task<IReadOnlyList<ServerAccess>> LoadServerAccessesAsync(int userId, CancellationToken cancellationToken)
    {
        return await _db.ServerAccesses
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    private async Task<(string? Username, string? Uuid)> ResolveMinecraftProfileAsync(
        string? minecraftUsername,
        CancellationToken cancellationToken)
    {
        if (minecraftUsername == null)
        {
            return (null, null);
        }

        var trimmed = minecraftUsername.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (null, null);
        }

        var profile = await _mojangApiService.LookupByUsernameAsync(trimmed, cancellationToken);
        if (profile == null)
        {
            throw new ArgumentException($"Minecraft user '{trimmed}' was not found");
        }

        return (profile.Name, profile.Uuid);
    }

    private async Task SyncServerAccessesAsync(
        int userId,
        IReadOnlyList<ServerAccessRequestDto> requestAccesses,
        CancellationToken cancellationToken)
    {
        var normalized = requestAccesses
            .Where(x => !string.IsNullOrWhiteSpace(x.ServerName))
            .Select(x => new ServerAccessRequestDto(
                x.ServerName.Trim(),
                x.CanView || x.CanControl || x.CanConsole,
                x.CanControl,
                x.CanConsole))
            .GroupBy(x => x.ServerName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var existing = await _db.ServerAccesses
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var normalizedNames = new HashSet<string>(normalized.Select(x => x.ServerName), StringComparer.OrdinalIgnoreCase);

        var toRemove = existing.Where(x => !normalizedNames.Contains(x.ServerName)).ToList();
        if (toRemove.Count > 0)
        {
            _db.ServerAccesses.RemoveRange(toRemove);
        }

        foreach (var access in normalized)
        {
            var record = existing.FirstOrDefault(x => x.ServerName.Equals(access.ServerName, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                _db.ServerAccesses.Add(new ServerAccess
                {
                    UserId = userId,
                    ServerName = access.ServerName,
                    CanView = access.CanView,
                    CanControl = access.CanControl,
                    CanConsole = access.CanConsole,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                record.CanView = access.CanView;
                record.CanControl = access.CanControl;
                record.CanConsole = access.CanConsole;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
