using Rustex.Domain.Entities;

namespace Rustex.Domain.Abstractions;

/// <summary>Posts a Discord-formatted embed to a webhook URL. Discord webhooks are a simple,
/// fully-documented HTTP POST — unlike Rust+ or Steam APIs, there's no undocumented protocol
/// or credential handshake here, so this is a real, complete implementation, not a stub.</summary>
public interface IDiscordWebhookSender
{
    Task SendRaidAlertAsync(string webhookUrl, RaidEvent raidEvent, string serverName, CancellationToken ct);

    /// <summary>A plain embed, for callers that aren't a RaidEvent (shop alerts, Rust+ pairing,
    /// smart alarms). <paramref name="color"/> is a 24-bit RGB value, e.g. 0xD97745.</summary>
    Task SendEmbedAsync(string webhookUrl, string title, string description, int color, CancellationToken ct);
}
