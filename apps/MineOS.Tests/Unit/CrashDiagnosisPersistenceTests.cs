// apps/MineOS.Tests/Unit/CrashDiagnosisPersistenceTests.cs
using Microsoft.EntityFrameworkCore;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Tests.Unit;

public class CrashDiagnosisPersistenceTests
{
    private static AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared")
            .Options;
        var db = new AppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static CrashDiagnosis Sample(string server, string hash) => new()
    {
        CrashEventId = 1,
        ServerName = server,
        CreatedAt = DateTimeOffset.UtcNow,
        SourceHash = hash,
        Model = "test-model",
        RedactedInput = "boom",
        Status = "complete"
    };

    [Fact]
    public void TwoServersMayShareASourceHash()
    {
        // Identical modpacks crash identically. A global unique index would let
        // one server's diagnosis block another's.
        using var db = NewContext();
        db.CrashDiagnoses.Add(Sample("smp", "abc123"));
        db.CrashDiagnoses.Add(Sample("creative", "abc123"));

        db.SaveChanges();

        Assert.Equal(2, db.CrashDiagnoses.Count());
    }

    [Fact]
    public void TheSameServerCannotStoreTheSameHashTwice()
    {
        using var db = NewContext();
        db.CrashDiagnoses.Add(Sample("smp", "abc123"));
        db.SaveChanges();

        db.CrashDiagnoses.Add(Sample("smp", "abc123"));

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }
}
