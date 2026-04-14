using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Tests.Integration;

public class DatabaseSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public DatabaseSchemaTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.Migrate();
    }

    [Fact]
    public void Migrations_Apply_Successfully()
    {
        var pendingMigrations = _context.Database.GetPendingMigrations();
        Assert.Empty(pendingMigrations);
    }

    [Fact]
    public void All_Migrations_Are_Applied()
    {
        var appliedMigrations = _context.Database.GetAppliedMigrations().ToList();
        Assert.Contains(appliedMigrations, m => m.Contains("AddLinkedAccounts"));
        Assert.Contains(appliedMigrations, m => m.Contains("AddCronJobs"));
    }

    [Fact]
    public void CronJob_Table_Exists_And_Accepts_Data()
    {
        var job = new CronJob
        {
            ServerName = "test-server",
            CronExpression = "0 * * * *",
            Action = "restart",
            Message = "Hourly restart",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Set<CronJob>().Add(job);
        _context.SaveChanges();

        var loaded = _context.Set<CronJob>().First();
        Assert.Equal("test-server", loaded.ServerName);
        Assert.Equal("0 * * * *", loaded.CronExpression);
        Assert.Equal("restart", loaded.Action);
        Assert.True(loaded.Enabled);
    }

    [Fact]
    public void LinkedAccount_Table_Exists_And_Accepts_Data()
    {
        var account = new LinkedAccount
        {
            AccessToken = "test-token",
            TokenType = "Bearer",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            UserId = "user-123",
            InstallationId = "install-456",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _context.Set<LinkedAccount>().Add(account);
        _context.SaveChanges();

        var loaded = _context.Set<LinkedAccount>().First();
        Assert.Equal("user-123", loaded.UserId);
        Assert.Equal("install-456", loaded.InstallationId);
    }

    [Fact]
    public void CronJob_Can_Be_Queried_By_ServerName()
    {
        _context.Set<CronJob>().AddRange(
            new CronJob { ServerName = "server-a", CronExpression = "0 * * * *", Action = "restart", CreatedAt = DateTimeOffset.UtcNow },
            new CronJob { ServerName = "server-b", CronExpression = "0 0 * * *", Action = "backup", CreatedAt = DateTimeOffset.UtcNow },
            new CronJob { ServerName = "server-a", CronExpression = "*/5 * * * *", Action = "backup", CreatedAt = DateTimeOffset.UtcNow }
        );
        _context.SaveChanges();

        var serverAJobs = _context.Set<CronJob>().Where(j => j.ServerName == "server-a").ToList();
        Assert.Equal(2, serverAJobs.Count);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
