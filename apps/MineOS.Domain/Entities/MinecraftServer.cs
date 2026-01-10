namespace MineOS.Domain.Entities;

public enum ServerStatus
{
    Unknown,
    Stopped,
    Running,
    Starting,
    Stopping
}

public class MinecraftServer
{
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required int OwnerUid { get; init; }
    public required int OwnerGid { get; init; }
    public required string ServerPath { get; init; }
    public required string BackupPath { get; init; }
    public required string ArchivePath { get; init; }
    public ServerStatus Status { get; set; }
    public int? JavaPid { get; set; }
    public int? ScreenPid { get; set; }
}
