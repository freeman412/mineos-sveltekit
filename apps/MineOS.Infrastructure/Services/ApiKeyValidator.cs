using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Infrastructure.Services;

public sealed class ApiKeyValidator : IApiKeyValidator
{
    private readonly AppDbContext _db;
    private readonly ApiKeyOptions _options;

    public ApiKeyValidator(AppDbContext db, IOptions<ApiKeyOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public Task<bool> IsValidAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.StaticKey) &&
            string.Equals(_options.StaticKey, apiKey, StringComparison.Ordinal))
        {
            return Task.FromResult(true);
        }

        return _db.ApiKeys.AnyAsync(k => k.IsActive && k.Key == apiKey, cancellationToken);
    }
}
