using MineOS.Domain.Entities;

namespace MineOS.Application.Interfaces;

public interface IModpackRepository
{
    Task<List<InstalledModRecord>> GetModsByServerWithModpackAsync(string serverName, CancellationToken ct);
    Task<List<InstalledModpack>> GetModpacksByServerAsync(string serverName, CancellationToken ct);
    Task<InstalledModpack?> GetModpackWithModsAsync(int modpackId, string serverName, CancellationToken ct);
    Task RemoveModpackAsync(InstalledModpack modpack, CancellationToken ct);
    Task UpsertModpackAsync(
        string serverName, string source, string sourceProjectId,
        string modpackName, string? modpackVersion, string? logoUrl, int? curseForgeProjectId,
        List<InstalledModRecord> modRecords, CancellationToken ct);
    Task AddModpackAsync(
        string serverName, string source, string sourceProjectId,
        string modpackName, string? modpackVersion, string? logoUrl, int? curseForgeProjectId,
        List<InstalledModRecord> modRecords, CancellationToken ct);
}
