using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Services;
using Moq;

namespace MineOS.Tests.Unit;

/// <summary>
/// Covers the per-server gate around ServerService.StartServerAsync.
///
/// The "is it already running?" guard is a check-then-act. A freshly launched screen
/// session does not appear in the process table instantly, and the work between the
/// check and the launch is not trivial, so two callers arriving inside that window
/// both saw "not running" and both launched. Four callers can race: the start
/// endpoint, WatchdogService, StartupServerService and CronSchedulerService.
///
/// Observed in the wild as two "Launching Serverloco" stamps 9ms apart, the second
/// Velocity instance dying with EADDRINUSE on a port its own twin had taken. On a
/// game server the same race puts two JVMs on one world directory.
///
/// The same gate now covers stop and kill. Gating starts alone closed only half the
/// window: stop sends its command and then polls for the process to disappear, and
/// took no gate, so a start could run against a server midway through shutting down
/// and a stop could be sent to a JVM still coming up. Both touch one world directory.
///
/// These tests assert on the ordering of the running-check itself rather than on a
/// launch, so they do not need `screen` on PATH: both calls fail after the check, and
/// what matters is that the second never enters while the first is still inside.
/// </summary>
public class ServerStartConcurrencyTests
{
    [Fact]
    public async Task SecondStartWaitsWhileTheFirstIsStillChecking()
    {
        var firstEntered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var entries = 0;

        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(m => m.IsServerRunningAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref entries) == 1)
                {
                    firstEntered.SetResult();
                    await release.Task;
                }
                return false;
            });

        var service = CreateService(processManager.Object);

        // Unique name: the gate is static and keyed by server name.
        const string name = "gate-test-waits";
        var first = Swallow(service.StartServerAsync(name, CancellationToken.None));
        await firstEntered.Task;

        var second = Swallow(service.StartServerAsync(name, CancellationToken.None));

        // The first caller is parked inside the guard. Without the gate the second
        // would sail into the same check and both would go on to launch.
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref entries));

        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, Volatile.Read(ref entries));
    }

    [Fact]
    public async Task DifferentServersAreNotBlockedByEachOther()
    {
        // The gate is per server: one slow start must not stall every other server.
        var firstEntered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var otherEntered = new TaskCompletionSource();

        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(m => m.IsServerRunningAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string server, CancellationToken _) =>
            {
                if (server == "gate-test-slow")
                {
                    firstEntered.SetResult();
                    await release.Task;
                }
                else
                {
                    otherEntered.TrySetResult();
                }
                return false;
            });

        var service = CreateService(processManager.Object);

        var slow = Swallow(service.StartServerAsync("gate-test-slow", CancellationToken.None));
        await firstEntered.Task;

        var other = Swallow(service.StartServerAsync("gate-test-other", CancellationToken.None));

        // Completes while the slow server is still holding its own gate.
        await otherEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        release.SetResult();
        await Task.WhenAll(slow, other);
    }

    [Fact]
    public async Task StopWaitsForAStartThatIsStillInFlight()
    {
        // The window this closes: a stop command reaching a JVM that is still starting.
        var startEntered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var order = new List<string>();

        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(m => m.IsServerRunningAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                lock (order) order.Add("check");
                if (order.Count == 1)
                {
                    startEntered.SetResult();
                    await release.Task;
                }
                return false;
            });

        var service = CreateService(processManager.Object);
        const string name = "gate-test-stop-waits";

        var start = Swallow(service.StartServerAsync(name, CancellationToken.None));
        await startEntered.Task;

        var stop = Swallow(service.StopServerAsync(name, 1, CancellationToken.None));

        await Task.Delay(100);
        lock (order) Assert.Single(order);

        release.SetResult();
        await Task.WhenAll(start, stop);
    }

    [Fact]
    public async Task StartIsRefusedWhileAnotherOperationHoldsTheGate()
    {
        // Bounded wait, not an infinite one: a stop can legitimately hold the gate for
        // its whole shutdown timeout, and parking an HTTP request behind that is how a
        // proxy in front of MineOS ends up timing the request out.
        var startEntered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(m => m.IsServerRunningAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (!startEntered.Task.IsCompleted)
                {
                    startEntered.SetResult();
                    await release.Task;
                }
                return false;
            });

        var service = CreateService(processManager.Object);
        const string name = "gate-test-busy";

        var held = Swallow(service.StartServerAsync(name, CancellationToken.None));
        await startEntered.Task;

        // Deliberately waits out the gate's own timeout rather than cancelling the
        // token, which would raise OperationCanceledException and prove nothing about
        // the busy path. Costs a few seconds; this is the behaviour the action endpoint
        // turns into a 409.
        var busy = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StopServerAsync(name, 1, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Contains("busy", busy.Message, StringComparison.OrdinalIgnoreCase);

        release.SetResult();
        await held;
    }

    /// <summary>
    /// The calls fail once past the guard - no server directory, no screen on PATH.
    /// Only the ordering of the guard itself is under test.
    /// </summary>
    private static async Task Swallow(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Expected.
        }
    }

    private static ServerService CreateService(IProcessManager processManager)
    {
        var options = Options.Create(new HostOptions
        {
            BaseDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        });

        return new ServerService(
            processManager,
            options,
            NullLogger<ServerService>.Instance,
            Mock.Of<ITelemetryService>(),
            Mock.Of<ITelemetryReportTrigger>(),
            Mock.Of<IDiscordWebhookService>());
    }
}
