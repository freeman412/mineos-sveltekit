using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Persistence;

public sealed class ApiKeySeeder
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ApiKeySeeder> _logger;

    public ApiKeySeeder(AppDbContext db, IConfiguration config, ILogger<ApiKeySeeder> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task EnsureSeedAsync(CancellationToken cancellationToken)
    {
        var staticKey = _config["ApiKey:StaticKey"];
        if (!string.IsNullOrWhiteSpace(staticKey))
        {
            _logger.LogInformation("Static API key configured; skipping API key seed.");
            return;
        }

        if (await _db.ApiKeys.AnyAsync(cancellationToken))
        {
            return;
        }

        var seedKey = _config["ApiKey:SeedKey"];
        if (string.IsNullOrWhiteSpace(seedKey))
        {
            seedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        var apiKey = new ApiKey
        {
            UserId = 1, // Will be associated with first user
            Key = seedKey.Trim(),
            Name = "default",
            Permissions = """["*"]""", // Full permissions
            CreatedAt = DateTimeOffset.UtcNow,
            Revoked = false
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded API key: {ApiKey}", apiKey.Key);
    }
}
