using Microsoft.Extensions.Options;
using MineOS.Application.Options;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class ServerPathProviderTests
{
    private static ServerPathProvider Build() => new(Options.Create(new HostOptions
    {
        BaseDirectory = "/var/games/minecraft",
        ServersPathSegment = "servers"
    }));

    [Fact]
    public void BuildsServerLogAndCrashReportPaths()
    {
        var paths = Build();

        Assert.Equal(Path.Combine("/var/games/minecraft", "servers", "smp"), paths.GetServerPath("smp"));
        Assert.Equal(Path.Combine("/var/games/minecraft", "servers", "smp", "logs", "latest.log"), paths.GetLogPath("smp"));
        Assert.Equal(Path.Combine("/var/games/minecraft", "servers", "smp", "crash-reports"), paths.GetCrashReportsPath("smp"));
    }
}
