using Rustex.Domain.Entities;

namespace Rustex.Domain.Abstractions;

/// <summary>Posts a Discord-formatted embed to a webhook URL. Discord webhooks are a simple,
/// fully-documented HTTP POST — unlike Rust+ or Steam APIs, there's no undocumented protocol
/// or credential handshake here, so this is a real, complete implementation, not a stub.</summary>
public interface IDiscordWebhookSender
{
    Task SendRaidAlertAsync(string webhookUrl, RaidEvent raidEvent, string serverName, CancellationToken ct);
}
