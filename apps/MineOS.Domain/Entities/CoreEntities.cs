namespace MineOS.Domain.Entities;

public sealed class ApiKey
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = "default";
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "admin";
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
