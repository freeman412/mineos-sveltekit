using Microsoft.EntityFrameworkCore;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;

namespace MineOS.Infrastructure.Persistence.Repositories;

public sealed class ModpackRepository : IModpackRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ModpackRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<InstalledModRecord>> GetModsByServerWithModpackAsync(
        string serverName, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.InstalledModRecords
            .AsNoTracking()
            .Where(r => r.ServerName == serverName)
            .Include(r => r.Modpack)
            .ToListAsync(ct);
    }

    public async Task<List<InstalledModpack>> GetModpacksByServerAsync(
        string serverName, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.InstalledModpacks
            .AsNoTracking()
            .Where(m => m.ServerName == serverName)
            .ToListAsync(ct);
    }

    public async Task<InstalledModpack?> GetModpackWithModsAsync(
        int modpackId, string serverName, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.InstalledModpacks
            .Include(m => m.Mods)
            .FirstOrDefaultAsync(m => m.Id == modpackId && m.ServerName == serverName, ct);
    }

    public async Task RemoveModpackAsync(InstalledModpack modpack, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.InstalledModpacks.Attach(modpack);
        db.InstalledModpacks.Remove(modpack);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertModpackAsync(
        string serverName, string source, string sourceProjectId,
        string modpackName, string? modpackVersion, string? logoUrl, int? curseForgeProjectId,
        List<InstalledModRecord> modRecords, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.InstalledModpacks
            .FirstOrDefaultAsync(m => m.ServerName == serverName &&
                                      m.Source == source &&
                                      m.SourceProjectId == sourceProjectId, ct);

        if (existing != null)
        {
            existing.CurseForgeProjectId = curseForgeProjectId;
            existing.Name = modpackName;
            existing.Version = modpackVersion;
            existing.LogoUrl = logoUrl;
            existing.ModCount = modRecords.Count;
            existing.InstalledAt = DateTimeOffset.UtcNow;

            var oldRecords = await db.InstalledModRecords
                .Where(r => r.ModpackId == existing.Id)
                .ToListAsync(ct);
            db.InstalledModRecords.RemoveRange(oldRecords);

            foreach (var record in modRecords)
            {
                record.ModpackId = existing.Id;
            }
            db.InstalledModRecords.AddRange(modRecords);
        }
        else
        {
            var modpack = new InstalledModpack
            {
                ServerName = serverName,
                CurseForgeProjectId = curseForgeProjectId,
                Source = source,
                SourceProjectId = sourceProjectId,
                Name = modpackName,
                Version = modpackVersion,
                LogoUrl = logoUrl,
                ModCount = modRecords.Count,
                InstalledAt = DateTimeOffset.UtcNow
            };
            db.InstalledModpacks.Add(modpack);
            await db.SaveChangesAsync(ct);

            foreach (var record in modRecords)
            {
                record.ModpackId = modpack.Id;
            }
            db.InstalledModRecords.AddRange(modRecords);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AddModpackAsync(
        string serverName, string source, string sourceProjectId,
        string modpackName, string? modpackVersion, string? logoUrl, int? curseForgeProjectId,
        List<InstalledModRecord> modRecords, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var modpack = new InstalledModpack
        {
            ServerName = serverName,
            CurseForgeProjectId = curseForgeProjectId,
            Source = source,
            SourceProjectId = sourceProjectId,
            Name = modpackName,
            Version = modpackVersion,
            LogoUrl = logoUrl,
            ModCount = modRecords.Count,
            InstalledAt = DateTimeOffset.UtcNow
        };

        db.InstalledModpacks.Add(modpack);
        await db.SaveChangesAsync(ct);

        foreach (var record in modRecords)
        {
            record.ModpackId = modpack.Id;
        }

        db.InstalledModRecords.AddRange(modRecords);
        await db.SaveChangesAsync(ct);
    }
}
