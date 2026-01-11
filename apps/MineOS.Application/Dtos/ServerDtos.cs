namespace MineOS.Application.Dtos;

public record CronJobDto(string Hash, string Source, string Action, string? Msg, bool Enabled);

public record NoticeDto(
    string Uuid,
    string Command,
    bool Success,
    string? Err,
    long TimeInitiated,
    long TimeResolved);

public record MemoryInfoDto(long? RssBytes);

public record PingInfoDto(
    int Protocol,
    string ServerVersion,
    string Motd,
    int PlayersOnline,
    int PlayersMax);

public record QueryInfoDto(Dictionary<string, object>? Raw);

public record ServerHeartbeatDto(
    string Name,
    string Status,
    int? JavaPid,
    int? ScreenPid,
    PingInfoDto? Ping,
    long? MemoryBytes);

public record ServerDetailDto(
    string Name,
    DateTimeOffset CreatedAt,
    int OwnerUid,
    int OwnerGid,
    string OwnerUsername,
    string OwnerGroupname,
    string Status,
    int? JavaPid,
    int? ScreenPid,
    ServerConfigDto? Config,
    bool EulaAccepted,
    bool NeedsRestart);

public record ServerConfigDto(
    JavaConfigDto Java,
    MinecraftConfigDto Minecraft,
    OnRebootConfigDto OnReboot);

public record JavaConfigDto(
    string JavaBinary,
    int JavaXmx,
    int JavaXms,
    string? JavaTweaks,
    string? JarFile,
    string? JarArgs);

public record MinecraftConfigDto(
    string? Profile,
    bool Unconventional);

public record OnRebootConfigDto(
    bool Start);

public record ConsoleCommandDto(string Command);

public record CreateServerRequest(string Name, int OwnerUid, int OwnerGid);

public record DeleteServerRequest(bool DeleteLive, bool DeleteBackups, bool DeleteArchives);

public record ActionRequest(string? Step, int? Niceness);

public record CreateCronRequest(string Source, string Action, string? Msg);

public record UpdateCronRequest(bool Enabled);

public record LoginRequestDto(string Username, string Password);

public record LoginResultDto(string AccessToken, int ExpiresInSeconds, string TokenType, string Username, string Role);
