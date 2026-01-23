using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Persistence;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests;

public sealed class UserServiceTests
{
    [Fact]
    public async Task CreateUserAsync_LinksMinecraftProfileAndAccesses()
    {
        await using var db = await CreateDbContextAsync();
        var service = CreateUserService(db, new FakeMojangApiService(new MojangProfileDto("uuid-123", "Notch", "https://example.com")));

        var result = await service.CreateUserAsync(
            new CreateUserRequestDto(
                "testuser",
                "password",
                "manager",
                "Notch",
                new[] { new ServerAccessRequestDto("Alpha", true, true, true) }),
            CancellationToken.None);

        Assert.Equal("Notch", result.MinecraftUsername);
        Assert.Equal("uuid-123", result.MinecraftUuid);
        Assert.Single(result.ServerAccesses);
        Assert.Equal("Alpha", result.ServerAccesses[0].ServerName);
    }

    [Fact]
    public async Task UpdateUserAsync_UpdatesServerAccesses()
    {
        await using var db = await CreateDbContextAsync();
        var service = CreateUserService(db, new FakeMojangApiService());

        var created = await service.CreateUserAsync(
            new CreateUserRequestDto("viewer", "password", "user", null, null),
            CancellationToken.None);

        var updated = await service.UpdateUserAsync(
            created.Id,
            new UpdateUserRequestDto(null, null, null, null, new[]
            {
                new ServerAccessRequestDto("Beta", true, false, false)
            }),
            CancellationToken.None);

        Assert.Single(updated.ServerAccesses);
        Assert.Equal("Beta", updated.ServerAccesses[0].ServerName);
        Assert.True(updated.ServerAccesses[0].CanView);
    }

    private static async Task<AppDbContext> CreateDbContextAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static UserService CreateUserService(AppDbContext db, IMojangApiService mojangApiService)
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        var logger = loggerFactory.CreateLogger<UserService>();
        return new UserService(db, new Argon2PasswordHasher(), mojangApiService, logger);
    }

    private sealed class FakeMojangApiService : IMojangApiService
    {
        private readonly Dictionary<string, MojangProfileDto> _profiles;

        public FakeMojangApiService(params MojangProfileDto[] profiles)
        {
            _profiles = profiles.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        }

        public Task<MojangProfileDto?> LookupByUsernameAsync(string username, CancellationToken cancellationToken)
        {
            _profiles.TryGetValue(username, out var profile);
            return Task.FromResult(profile);
        }

        public Task<MojangProfileDto?> LookupByUuidAsync(string uuid, CancellationToken cancellationToken)
        {
            var profile = _profiles.Values.FirstOrDefault(p => p.Uuid.Equals(uuid, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(profile);
        }
    }
}
