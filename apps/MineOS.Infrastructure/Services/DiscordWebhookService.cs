using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

/// <summary>
/// Delivers queued server events to the configured Discord webhook as rich
/// embeds. Fire-and-forget by design: producers enqueue and move on; delivery
/// failures are logged and never affect server operations. Sends are spaced
/// out and 429 responses honor Retry-After to stay inside Discord's webhook
/// rate limits.
/// </summary>
public sealed class DiscordWebhookService : BackgroundService, IDiscordWebhookService
{
    public const string HttpClientName = "discord-webhook";

    private static readonly TimeSpan MinSendSpacing = TimeSpan.FromSeconds(1);

    private readonly Channel<DiscordEvent> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiscordWebhookService> _logger;

    public DiscordWebhookService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<DiscordWebhookService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _queue = Channel.CreateBounded<DiscordEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public void QueueEvent(DiscordEvent evt)
    {
        _queue.Writer.TryWrite(evt);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var webhookUrl = await GetWebhookUrlAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(webhookUrl))
                {
                    continue; // Not configured — drop silently.
                }

                await SendAsync(webhookUrl, evt, stoppingToken);
                await Task.Delay(MinSendSpacing, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deliver Discord webhook for event {Title}", evt.Title);
            }
        }
    }

    private async Task<string?> GetWebhookUrlAsync(CancellationToken ct)
    {
        // ISettingsService is scoped (it reads the DB); resolve per delivery.
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        return await settings.GetAsync(SettingsService.Keys.DiscordWebhookUrl, ct);
    }

    private async Task SendAsync(string webhookUrl, DiscordEvent evt, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var content = new StringContent(BuildPayload(evt), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(webhookUrl, content, ct);

        if ((int)response.StatusCode == 429)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            _logger.LogWarning("Discord webhook rate limited; retrying after {Delay}", retryAfter);
            await Task.Delay(retryAfter, ct);

            content = new StringContent(BuildPayload(evt), Encoding.UTF8, "application/json");
            response = await client.PostAsync(webhookUrl, content, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Discord webhook returned {Status} for event {Title}",
                (int)response.StatusCode, evt.Title);
        }
    }

    /// <summary>
    /// Build the Discord webhook JSON payload (embed format) for an event.
    /// </summary>
    public static string BuildPayload(DiscordEvent evt)
    {
        var embed = new Dictionary<string, object?>
        {
            ["title"] = evt.Title,
            ["description"] = evt.Message,
            ["color"] = ColorFor(evt.Level),
            ["timestamp"] = evt.Timestamp.UtcDateTime.ToString("o"),
            ["footer"] = new { text = "MineOS" }
        };
        if (evt.ServerName is { Length: > 0 })
        {
            embed["fields"] = new[] { new { name = "Server", value = evt.ServerName, inline = true } };
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["username"] = "MineOS",
            ["embeds"] = new[] { embed }
        });
    }

    private static int ColorFor(DiscordEventLevel level) => level switch
    {
        DiscordEventLevel.Success => 0x6AB04C, // green
        DiscordEventLevel.Warning => 0xF59E0B, // amber
        DiscordEventLevel.Error => 0xEF4444,   // red
        _ => 0x5B9EFF                          // blue
    };
}
