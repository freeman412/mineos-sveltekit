namespace MineOS.Application.Options;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "MineOS";
    public string Audience { get; set; } = "MineOS.Web";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 60;
}
