using MineOS.Domain.ValueObjects;

namespace MineOS.Application.Interfaces;

public interface IContainerPortInspector
{
    /// <summary>
    /// Reports whether a port is reachable from outside the host.
    ///
    /// Implementations must return <see cref="ExposureVerdict.Unknown"/> — never
    /// <see cref="ExposureVerdict.NotExposed"/> — when they cannot actually tell.
    /// This answer is used as a safety control for backends that have no way to
    /// verify forwarded players, so guessing "probably fine" is the one behaviour
    /// that must not happen.
    /// </summary>
    Task<(ExposureVerdict Verdict, string? Detail)> IsPortPublishedAsync(int port, CancellationToken cancellationToken);
}
