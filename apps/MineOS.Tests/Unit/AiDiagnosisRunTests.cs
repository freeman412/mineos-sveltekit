// apps/MineOS.Tests/Unit/AiDiagnosisRunTests.cs
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class AiDiagnosisRunTests
{
    [Fact]
    public void ParsesAWellFormedResponse()
    {
        var payload = AiDiagnosisService.ParseResponse("""
            {"summary":"Mod conflict","likelyCause":"JEI needs API v3",
             "suggestedActions":["Update JEI","Remove X"],
             "classification":"mod-or-modpack","confidence":"high"}
            """);

        Assert.Equal("Mod conflict", payload.Summary);
        Assert.Equal(2, payload.SuggestedActions.Count);
        Assert.Equal("mod-or-modpack", payload.Classification);
        Assert.Equal("high", payload.Confidence);
    }

    [Fact]
    public void ToleratesAFencedCodeBlock()
    {
        // Models routinely wrap JSON in ```json fences despite instructions.
        var payload = AiDiagnosisService.ParseResponse("```json\n{\"summary\":\"Boom\"}\n```");

        Assert.Equal("Boom", payload.Summary);
    }

    [Fact]
    public void RejectsNonJsonRatherThanInventingADiagnosis()
    {
        Assert.Throws<FormatException>(() => AiDiagnosisService.ParseResponse("I think your server is sad."));
    }

    [Fact]
    public void DefaultsAnUnrecognisedClassificationToUnknown()
    {
        var payload = AiDiagnosisService.ParseResponse("""{"summary":"x","classification":"definitely-mineos"}""");

        Assert.Equal("unknown", payload.Classification);
    }
}
