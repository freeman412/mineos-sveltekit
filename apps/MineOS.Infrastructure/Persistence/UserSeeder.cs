using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Persistence;

public sealed class UserSeeder
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<UserSeeder> _logger;

    public UserSeeder(
        AppDbContext db,
        IConfiguration config,
        IPasswordHasher passwordHasher,
        ILogger<UserSeeder> logger)
    {
        _db = db;
        _config = config;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task EnsureSeedAsync(CancellationToken cancellationToken)
    {
        if (await _db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var seedUsername = _config["Auth:SeedUsername"];
        var seedPassword = _config["Auth:SeedPassword"];

        if (string.IsNullOrWhiteSpace(seedUsername) || string.IsNullOrWhiteSpace(seedPassword))
        {
            _logger.LogWarning("No seed user configured. Set Auth:SeedUsername and Auth:SeedPassword.");
            return;
        }

        var user = new User
        {
            Username = seedUsername.Trim(),
            PasswordHash = _passwordHasher.Hash(seedPassword),
            Role = "admin",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded user: {Username}", user.Username);
    }
}
