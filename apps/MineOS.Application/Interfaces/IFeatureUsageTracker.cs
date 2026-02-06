namespace MineOS.Application.Interfaces;

public interface IFeatureUsageTracker
{
    void Increment(string featureKey, int count = 1);
    Dictionary<string, int> GetAndReset();
}
