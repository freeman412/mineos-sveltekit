namespace MineOS.Domain.ValueObjects;

public record ServerConfiguration
{
    public string JavaBinary { get; init; } = string.Empty;
    public int JavaXmx { get; init; }
    public int JavaXms { get; init; }
    public string JarFile { get; init; } = string.Empty;
    public string? JavaTweaks { get; init; }
    public string? JarArgs { get; init; }
    public string? Profile { get; init; }
    public bool Unconventional { get; init; }
    public bool LanBroadcast { get; init; }
    public bool OnRebootStart { get; init; }
}
