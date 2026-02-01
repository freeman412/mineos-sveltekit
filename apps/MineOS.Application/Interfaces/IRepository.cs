using System.Linq.Expressions;

namespace MineOS.Application.Interfaces;

public interface IRepository<T> where T : class
{
    T? FindById(params object[] keyValues);
    Task<T?> FindByIdAsync(CancellationToken ct, params object[] keyValues);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct);
    Task<List<T>> ToListAsync(Expression<Func<T, bool>> predicate, CancellationToken ct);
    Task AddAsync(T entity, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct);
    Task UpdateAsync(T entity, CancellationToken ct);
    Task RemoveAsync(T entity, CancellationToken ct);
    Task RemoveWhereAsync(Expression<Func<T, bool>> predicate, CancellationToken ct);
}
