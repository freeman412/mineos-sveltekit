using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.External;

namespace MineOS.Tests.Unit;

public class OpenAiCompatibleClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    private const string ValidBody = """
        {"model":"test-model","choices":[{"message":{"content":"it broke"}}],
         "usage":{"prompt_tokens":10,"completion_tokens":5}}
        """;

    private static Mock<ISettingsService> Settings(string? apiKey = "sk-test")
    {
        var mock = new Mock<ISettingsService>();
        mock.Setup(s => s.GetAsync("Ai:Enabled", It.IsAny<CancellationToken>())).ReturnsAsync("true");
        mock.Setup(s => s.GetAsync("Ai:BaseUrl", It.IsAny<CancellationToken>())).ReturnsAsync("http://localhost:11434/v1");
        mock.Setup(s => s.GetAsync("Ai:Model", It.IsAny<CancellationToken>())).ReturnsAsync("test-model");
        mock.Setup(s => s.GetAsync("Ai:ApiKey", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        mock.Setup(s => s.GetAsync("Ai:MaxTokens", It.IsAny<CancellationToken>())).ReturnsAsync("1000");
        mock.Setup(s => s.GetAsync("Ai:TimeoutSeconds", It.IsAny<CancellationToken>())).ReturnsAsync("60");
        return mock;
    }

    private static OpenAiCompatibleClient Build(StubHandler handler, Mock<ISettingsService> settings) =>
        new(new HttpClient(handler), settings.Object, NullLogger<OpenAiCompatibleClient>.Instance);

    [Fact]
    public void BuildRequestPayload_UsesChatCompletionsShape()
    {
        var json = OpenAiCompatibleClient.BuildRequestPayload(
            new AiCompletionRequest("you are a helper", "why did it crash", 500), "test-model");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.Equal(500, root.GetProperty("max_tokens").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("you are a helper", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task SendsAuthorizationHeaderWhenKeyIsConfigured()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidBody);
        var result = await Build(handler, Settings()).CompleteAsync(new AiCompletionRequest("s", "u"), default);

        Assert.True(result.Success);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task OmitsAuthorizationHeaderWhenKeyIsBlank()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidBody);
        await Build(handler, Settings(apiKey: "")).CompleteAsync(new AiCompletionRequest("s", "u"), default);

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task IsConfigured_TrueWithoutAnApiKey()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidBody);
        Assert.True(await Build(handler, Settings(apiKey: null)).IsConfiguredAsync(default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiFailureReason.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, AiFailureReason.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, AiFailureReason.Transport)]
    public async Task MapsHttpStatusToTypedFailure(HttpStatusCode status, AiFailureReason expected)
    {
        var handler = new StubHandler(status, "{}");
        var result = await Build(handler, Settings()).CompleteAsync(new AiCompletionRequest("s", "u"), default);

        Assert.False(result.Success);
        Assert.Equal(expected, result.Failure);
    }
}
