using Microsoft.EntityFrameworkCore;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Infrastructure.Services;

public sealed class ServerAccessService : IServerAccessService
{
    private readonly AppDbContext _db;

    public ServerAccessService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ServerAccessDto?> GetAccessAsync(int userId, string serverName, CancellationToken cancellationToken)
    {
        var access = await _db.ServerAccesses
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.ServerName.ToLower() == serverName.ToLower(),
                cancellationToken);

        return access == null
            ? null
            : new ServerAccessDto(access.ServerName, access.CanView, access.CanControl, access.CanConsole);
    }

    public async Task<IReadOnlyList<ServerAccessDto>> ListAccessAsync(int userId, CancellationToken cancellationToken)
    {
        var accessList = await _db.ServerAccesses
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.ServerName)
            .ToListAsync(cancellationToken);

        return accessList
            .Select(access => new ServerAccessDto(access.ServerName, access.CanView, access.CanControl, access.CanConsole))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListServerNamesAsync(int userId, CancellationToken cancellationToken)
    {
        var serverNames = await _db.ServerAccesses
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.CanView)
            .Select(x => x.ServerName)
            .ToListAsync(cancellationToken);

        return serverNames;
    }
}
