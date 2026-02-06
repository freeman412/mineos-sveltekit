namespace MineOS.Domain.Entities;

public sealed class LinkedAccount
{
    public int Id { get; set; }
    public required string AccessToken { get; set; }
    public required string TokenType { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string UserId { get; set; }
    public required string InstallationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
