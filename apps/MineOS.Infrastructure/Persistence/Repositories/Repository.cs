using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Persistence.Repositories;

public sealed class Repository<T> : IRepository<T> where T : class
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public Repository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public T? FindById(params object[] keyValues)
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Set<T>().Find(keyValues);
    }

    public async Task<T?> FindByIdAsync(CancellationToken ct, params object[] keyValues)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Set<T>().FindAsync(keyValues, ct);
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(predicate, ct);
    }

    public async Task<List<T>> ToListAsync(Expression<Func<T, bool>> predicate, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Set<T>().AsNoTracking().Where(predicate).ToListAsync(ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Set<T>().Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Set<T>().AddRange(entities);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(T entity, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Set<T>().Update(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(T entity, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Set<T>().Attach(entity);
        db.Set<T>().Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveWhereAsync(Expression<Func<T, bool>> predicate, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entities = await db.Set<T>().Where(predicate).ToListAsync(ct);
        if (entities.Count > 0)
        {
            db.Set<T>().RemoveRange(entities);
            await db.SaveChangesAsync(ct);
        }
    }
}
