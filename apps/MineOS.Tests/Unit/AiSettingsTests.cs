// apps/MineOS.Tests/Unit/AiSettingsTests.cs
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class AiSettingsTests
{
    [Theory]
    [InlineData("Ai:Enabled")]
    [InlineData("Ai:BaseUrl")]
    [InlineData("Ai:ApiKey")]
    [InlineData("Ai:Model")]
    [InlineData("Ai:MaxTokens")]
    [InlineData("Ai:TimeoutSeconds")]
    [InlineData("Ai:MaxDiagnosesPerHour")]
    [InlineData("Ai:RedactPaths")]
    [InlineData("Ai:RedactPlayerNames")]
    public void EveryAiKeyHasMetadata(string key)
    {
        Assert.True(SettingsService.HasMetadata(key), $"{key} needs a SettingsMetadata entry to render in the UI");
    }

    [Fact]
    public void ApiKeyIsMarkedSecretAndBaseUrlIsNot()
    {
        Assert.True(SettingsService.IsSecretSetting("Ai:ApiKey"));
        Assert.False(SettingsService.IsSecretSetting("Ai:BaseUrl"));
    }
}
