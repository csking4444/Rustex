using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Rustex.Domain;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;

namespace Rustex.Infrastructure.Notifications;

public class DiscordWebhookSender : IDiscordWebhookSender
{
    private readonly HttpClient _http;
    private readonly ILogger<DiscordWebhookSender> _logger;

    public DiscordWebhookSender(HttpClient http, ILogger<DiscordWebhookSender> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task SendRaidAlertAsync(string webhookUrl, RaidEvent raidEvent, string serverName, CancellationToken ct)
    {
        var color = raidEvent.Tier switch
        {
            RaidTier.Tier3 => 0xC1121F,
            RaidTier.Tier2 => 0xD89A2B,
            _ => 0x4A7A96,
        };

        var description = raidEvent.Grid is not null
            ? $"Grid **{raidEvent.Grid}** · {raidEvent.ExplosionCount} explosion{(raidEvent.ExplosionCount == 1 ? "" : "s")} · {raidEvent.RaidType ?? "unknown"}"
            : $"{raidEvent.ExplosionCount} explosion{(raidEvent.ExplosionCount == 1 ? "" : "s")} · {raidEvent.RaidType ?? "unknown"}";

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title = $"{TierLabel(raidEvent.Tier)} raid detected — {serverName}",
                    description,
                    color,
                    timestamp = raidEvent.DetectedAt.ToString("o"),
                },
            },
        };

        try
        {
            var response = await _http.PostAsJsonAsync(webhookUrl, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Discord webhook post to {Url} failed with {Status}",
                    webhookUrl, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // A broken webhook must never block the other notification channels — see
            // EmergencyAlertDispatcher, which calls this after the in-app/SignalR paths.
            _logger.LogWarning(ex, "Discord webhook post to {Url} threw", webhookUrl);
        }
    }

    public async Task SendEmbedAsync(string webhookUrl, string title, string description, int color, CancellationToken ct)
    {
        var payload = new { embeds = new[] { new { title, description, color, timestamp = DateTimeOffset.UtcNow.ToString("o") } } };

        try
        {
            var response = await _http.PostAsJsonAsync(webhookUrl, payload, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Discord webhook post to {Url} failed with {Status}", webhookUrl, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord webhook post to {Url} threw", webhookUrl);
        }
    }

    private static string TierLabel(RaidTier tier) => tier switch
    {
        RaidTier.Tier3 => "Tier 3",
        RaidTier.Tier2 => "Tier 2",
        _ => "Tier 1",
    };
}
