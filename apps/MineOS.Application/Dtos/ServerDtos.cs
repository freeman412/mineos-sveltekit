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

public record PingInfoDto(string? ServerVersion, string? Motd, int? PlayersOnline, int? PlayersMax);

public record QueryInfoDto(Dictionary<string, object>? Raw);

public record ServerHeartbeatDto(
    bool Up,
    MemoryInfoDto? Memory,
    PingInfoDto? Ping,
    QueryInfoDto? Query,
    long Timestamp);

public record ConsoleCommandDto(string Command);

public record CreateServerRequest(string ServerName, Dictionary<string, string>? Properties, bool Unconventional);

public record DeleteServerRequest(bool DeleteLive, bool DeleteBackups, bool DeleteArchives);

public record ActionRequest(string? Step, int? Niceness);

public record CreateCronRequest(string Source, string Action, string? Msg);

public record UpdateCronRequest(bool Enabled);

public record LoginRequestDto(string Username, string Password);

public record LoginResultDto(string AccessToken, int ExpiresInSeconds, string TokenType, string Username, string Role);
