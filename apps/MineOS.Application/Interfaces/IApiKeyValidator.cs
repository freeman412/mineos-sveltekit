namespace MineOS.Application.Interfaces;

public interface IApiKeyValidator
{
    Task<bool> IsValidAsync(string apiKey, CancellationToken cancellationToken);
}
