using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MineOS.Application.Interfaces;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class DiscordWebhookServiceTests
{
    private static DiscordEvent SampleEvent(string? server = "lobby") => new(
        "Server Crashed",
        "lobby crashed (OutOfMemory).",
        DiscordEventLevel.Error,
        server,
        new DateTimeOffset(2026, 7, 22, 3, 0, 0, TimeSpan.Zero));

    [Fact]
    public void BuildPayload_ProducesDiscordEmbed()
    {
        var json = DiscordWebhookService.BuildPayload(SampleEvent());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("MineOS", root.GetProperty("username").GetString());

        var embed = root.GetProperty("embeds")[0];
        Assert.Equal("Server Crashed", embed.GetProperty("title").GetString());
        Assert.Equal("lobby crashed (OutOfMemory).", embed.GetProperty("description").GetString());
        Assert.Equal(0xEF4444, embed.GetProperty("color").GetInt32());
        Assert.StartsWith("2026-07-22T03:00:00", embed.GetProperty("timestamp").GetString());

        var field = embed.GetProperty("fields")[0];
        Assert.Equal("Server", field.GetProperty("name").GetString());
        Assert.Equal("lobby", field.GetProperty("value").GetString());
    }

    [Fact]
    public void BuildPayload_OmitsServerFieldForGlobalEvents()
    {
        var json = DiscordWebhookService.BuildPayload(SampleEvent(server: null));
        using var doc = JsonDocument.Parse(json);
        var embed = doc.RootElement.GetProperty("embeds")[0];
        Assert.False(embed.TryGetProperty("fields", out _));
    }

    [Theory]
    [InlineData("success", DiscordEventLevel.Success)]
    [InlineData("warning", DiscordEventLevel.Warning)]
    [InlineData("error", DiscordEventLevel.Error)]
    [InlineData("ERROR", DiscordEventLevel.Error)]
    [InlineData("info", DiscordEventLevel.Info)]
    [InlineData("anything", DiscordEventLevel.Info)]
    [InlineData(null, DiscordEventLevel.Info)]
    public void LevelFromNotificationType_Maps(string? type, DiscordEventLevel expected)
    {
        Assert.Equal(expected, DiscordEvent.LevelFromNotificationType(type));
    }

    [Fact]
    public async Task QueuedEvent_IsPostedToConfiguredWebhook()
    {
        var handler = new CapturingHandler();
        var service = BuildService(handler, webhookUrl: "https://discord.example/api/webhooks/1/x");

        await service.StartAsync(CancellationToken.None);
        try
        {
            service.QueueEvent(SampleEvent());

            var (uri, body) = await handler.WaitForRequestAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("https://discord.example/api/webhooks/1/x", uri);
            Assert.Contains("Server Crashed", body);
            Assert.Contains("embeds", body);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UnconfiguredWebhook_DropsEventsWithoutSending()
    {
        var handler = new CapturingHandler();
        var service = BuildService(handler, webhookUrl: null);

        await service.StartAsync(CancellationToken.None);
        try
        {
            service.QueueEvent(SampleEvent());
            await Task.Delay(300);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static DiscordWebhookService BuildService(CapturingHandler handler, string? webhookUrl)
    {
        var settings = new Mock<ISettingsService>();
        settings
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhookUrl);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => settings.Object);
        services.AddHttpClient(DiscordWebhookService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<DiscordWebhookService>();

        return services.BuildServiceProvider().GetRequiredService<DiscordWebhookService>();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<(string Uri, string Body)> _first = new();
        private int _count;

        public int RequestCount => _count;

        public Task<(string Uri, string Body)> WaitForRequestAsync(TimeSpan timeout) =>
            _first.Task.WaitAsync(timeout);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            _first.TrySetResult((request.RequestUri!.ToString(), body));
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
    }
}
