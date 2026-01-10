namespace MineOS.Domain.ValueObjects;

public record ServerProperties
{
    public int ServerPort { get; init; } = 25565;
    public int MaxPlayers { get; init; } = 20;
    public string LevelSeed { get; init; } = string.Empty;
    public int Gamemode { get; init; } = 0;
    public int Difficulty { get; init; } = 1;
    public string LevelType { get; init; } = "DEFAULT";
    public string LevelName { get; init; } = "world";
    public int MaxBuildHeight { get; init; } = 256;
    public bool GenerateStructures { get; init; } = true;
    public string GeneratorSettings { get; init; } = string.Empty;
    public string ServerIp { get; init; } = "0.0.0.0";
    public bool EnableQuery { get; init; }

    // Store all properties as dictionary for flexibility
    public Dictionary<string, string> AllProperties { get; init; } = new();
}
