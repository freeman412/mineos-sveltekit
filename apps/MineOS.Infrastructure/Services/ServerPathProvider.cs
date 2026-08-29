using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class ServerPathProvider : IServerPathProvider
{
    private readonly HostOptions _hostOptions;

    public ServerPathProvider(IOptions<HostOptions> hostOptions) => _hostOptions = hostOptions.Value;

    public string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    public string GetLogPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "logs", "latest.log");

    public string GetCrashReportsPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "crash-reports");
}
