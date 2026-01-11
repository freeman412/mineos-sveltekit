namespace MineOS.Application.Options;

public sealed class HostOptions
{
    public string BaseDirectory { get; set; } = "/var/games/minecraft";
    public string LocalesPath { get; set; } = "html/locales";
    public string ServersPathSegment { get; set; } = "servers";
    public string ProfilesPathSegment { get; set; } = "profiles";
    public string ImportPathSegment { get; set; } = "import";
    public string BackupsPathSegment { get; set; } = "backups";
    public int RunAsUid { get; set; } = 1000;
    public int RunAsGid { get; set; } = 1000;
}
