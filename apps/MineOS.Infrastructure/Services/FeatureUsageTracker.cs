using System.Collections.Concurrent;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public sealed class FeatureUsageTracker : IFeatureUsageTracker
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public void Increment(string featureKey, int count = 1)
    {
        _counts.AddOrUpdate(featureKey, count, (_, existing) => existing + count);
    }

    public Dictionary<string, int> GetAndReset()
    {
        var result = new Dictionary<string, int>();

        foreach (var key in _counts.Keys.ToList())
        {
            if (_counts.TryRemove(key, out var count) && count > 0)
            {
                result[key] = count;
            }
        }

        return result;
    }
}
