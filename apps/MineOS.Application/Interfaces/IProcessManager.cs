namespace MineOS.Application.Interfaces;

public interface IProcessManager
{
    // Process discovery
    Dictionary<string, ServerProcessInfo> GetServerProcesses();
    ServerProcessInfo? GetServerProcess(string serverName);

    // Screen session management
    Task StartScreenSessionAsync(string serverName, string[] args, int uid, int gid, string workingDirectory, CancellationToken cancellationToken);
    Task SendCommandAsync(string serverName, string command, int uid, int gid, CancellationToken cancellationToken);
    Task SendScreenCommandAsync(string sessionName, string command, int uid, int gid, CancellationToken cancellationToken);
    Task<bool> IsServerRunningAsync(string serverName, CancellationToken cancellationToken);

    // Process control
    Task KillProcessAsync(int pid, CancellationToken cancellationToken);
}

public record ServerProcessInfo(int? JavaPid, int? ScreenPid);
