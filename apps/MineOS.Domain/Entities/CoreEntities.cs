namespace MineOS.Domain.Entities;

public sealed class ApiKey
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Permissions { get; set; } // JSON array of permissions
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Revoked { get; set; }
}

public sealed class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public string? MinecraftUsername { get; set; }
    public string? MinecraftUuid { get; set; }
    public string Role { get; set; } = "admin";
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ServerAccess
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string ServerName { get; set; }
    public bool CanView { get; set; } = true;
    public bool CanControl { get; set; } = true;
    public bool CanConsole { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
