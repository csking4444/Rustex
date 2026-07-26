using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rustex.Domain;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Infrastructure.Emergency;

/// <summary>
/// After EventIngestionWorker persists a qualifying RaidEvent, decides who to alert and how.
/// Phase 1/2/3 scope: only the server owner is considered (team-wide alerting is Phase 7).
///
/// Respects the user's UserSettings: CallEnabled/DesktopEnabled toggles, and a quiet-hours
/// window (timezone-aware, handles midnight wraparound) that mutes the ring alert specifically
/// — a muted ring still falls back to a desktop notification rather than going silent entirely.
///
/// Delivery channel: a user with a live "App" (installed/standalone) connection gets a
/// full-screen, looping-audio ring alert; otherwise they get a plain browser notification. This
/// deliberately does *not* place a real PSTN phone call — a browser/PWA has no way to register
/// with a device's native telephony stack, so the "call" experience here is the closest
/// approximation buildable without a native app shell (see docs/ARCHITECTURE.md). The
/// Twilio/Vonage/Plivo IVoiceCallProvider abstraction and CallAlert/PhoneNumber schema are
/// still there for a real PSTN channel later, but are not the primary path anymore.
/// </summary>
public class EmergencyAlertDispatcher : IEmergencyAlertDispatcher
{
    private const string RaidDetectedTrigger = "RaidDetected";
    private const string RaidAlertNotificationType = "raid_alert";

    private readonly AppDbContext _db;
    private readonly IClientConnectionRegistry _connectionRegistry;
    private readonly IRaidEventBroadcaster _broadcaster;
    private readonly IDiscordWebhookSender _discordWebhookSender;
    private readonly IWebPushSender _webPushSender;
    private readonly ILogger<EmergencyAlertDispatcher> _logger;

    public EmergencyAlertDispatcher(
        AppDbContext db,
        IClientConnectionRegistry connectionRegistry,
        IRaidEventBroadcaster broadcaster,
        IDiscordWebhookSender discordWebhookSender,
        IWebPushSender webPushSender,
        ILogger<EmergencyAlertDispatcher> logger)
    {
        _db = db;
        _connectionRegistry = connectionRegistry;
        _broadcaster = broadcaster;
        _discordWebhookSender = discordWebhookSender;
        _webPushSender = webPushSender;
        _logger = logger;
    }

    public async Task DispatchAsync(RaidEvent raidEvent, CancellationToken ct)
    {
        var server = await _db.RustServers.FirstOrDefaultAsync(s => s.Id == raidEvent.ServerId, ct);
        if (server is null) return;

        var userId = server.OwnerUserId;
        var settings = await ResolveSettingsAsync(userId, server.Id, ct);

        if (settings is not null && !settings.IsEnabled) return;

        var minTier = settings?.MinTier ?? RaidTier.Tier1;
        if (raidEvent.Tier < minTier) return;

        var cooldownSeconds = settings?.CooldownSeconds ?? 120;
        var lastAlert = await _db.Notifications
            .Where(n => n.UserId == userId && n.Type == RaidAlertNotificationType)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastAlert is not null && DateTimeOffset.UtcNow - lastAlert.CreatedAt < TimeSpan.FromSeconds(cooldownSeconds))
        {
            _logger.LogDebug("Suppressing emergency alert for user {UserId}: within {Cooldown}s cooldown", userId, cooldownSeconds);
            return;
        }

        var notification = new Notification
        {
            UserId = userId,
            Type = RaidAlertNotificationType,
            Title = $"{TierLabel(raidEvent.Tier)} raid detected — {server.Name}",
            Body = raidEvent.Grid is not null
                ? $"Grid {raidEvent.Grid} · {raidEvent.ExplosionCount} explosion{(raidEvent.ExplosionCount == 1 ? "" : "s")}"
                : $"{raidEvent.ExplosionCount} explosion{(raidEvent.ExplosionCount == 1 ? "" : "s")}",
            Severity = raidEvent.Tier switch
            {
                RaidTier.Tier3 => NotificationSeverity.Critical,
                RaidTier.Tier2 => NotificationSeverity.Warning,
                _ => NotificationSeverity.Info,
            },
            RelatedEntityType = nameof(RaidEvent),
            RelatedEntityId = raidEvent.Id,
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        var payload = new
        {
            raidEvent.Id,
            raidEvent.ServerId,
            ServerName = server.Name,
            raidEvent.Tier,
            raidEvent.Grid,
            raidEvent.ExplosionCount,
            raidEvent.RaidType,
            raidEvent.DetectedAt,
        };

        var userSettings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        var inQuietHours = userSettings is not null && IsWithinQuietHours(userSettings);

        var activeKinds = _connectionRegistry.GetActiveKinds(userId);
        var wantsRingAlert = activeKinds.Contains(ClientKind.App) && !inQuietHours && (userSettings?.CallEnabled ?? true);

        if (wantsRingAlert)
        {
            await _broadcaster.BroadcastIncomingRaidCallAsync(userId, payload, ct);
        }
        else if (userSettings?.DesktopEnabled ?? true)
        {
            // Also the fallback for App-kind users during quiet hours or with ring alerts
            // disabled — quiet hours mute the *ring*, not the notification entirely.
            await _broadcaster.BroadcastRaidAlertNotificationAsync(userId, payload, ct);
        }

        if (userSettings?.DiscordEnabled ?? false)
        {
            var webhooks = await _db.Webhooks
                .Where(w => w.ServerId == server.Id && w.IsActive && w.EventTypes.Contains(RaidDetectedTrigger))
                .ToListAsync(ct);

            foreach (var webhook in webhooks)
                await _discordWebhookSender.SendRaidAlertAsync(webhook.Url, raidEvent, server.Name, ct);
        }

        if ((userSettings?.PushEnabled ?? false) && _webPushSender.IsConfigured)
            await SendWebPushAsync(userId, notification, ct);
    }

    /// <summary>Unlike the SignalR channels above, Web Push reaches a subscribed browser/PWA
    /// even when it's backgrounded or fully closed (that's the entire point of the Push API) —
    /// so this fires regardless of ClientConnectionRegistry state. A dead subscription (push
    /// service returns 404/410) is deleted so it stops being retried on future alerts.</summary>
    private async Task SendWebPushAsync(Guid userId, Notification notification, CancellationToken ct)
    {
        var subscriptions = await _db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = notification.Title,
            body = notification.Body,
            notificationId = notification.Id,
        });

        foreach (var subscription in subscriptions)
        {
            var result = await _webPushSender.SendAsync(subscription, payloadJson, ct);
            if (result == WebPushResult.SubscriptionExpired)
                _db.PushSubscriptions.Remove(subscription);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Compares "now" against QuietHoursStart/End in the user's configured timezone.
    /// Handles windows that cross midnight (e.g. 22:00-08:00).</summary>
    private static bool IsWithinQuietHours(UserSettings settings)
    {
        if (settings.QuietHoursStart is null || settings.QuietHoursEnd is null) return false;

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.QuietHoursTimezone);
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localNow = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
        var start = settings.QuietHoursStart.Value;
        var end = settings.QuietHoursEnd.Value;

        return start <= end
            ? localNow >= start && localNow < end
            : localNow >= start || localNow < end;
    }

    private async Task<CallAlertSetting?> ResolveSettingsAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        var candidates = await _db.CallAlertSettings
            .Where(s => s.UserId == userId && s.TriggerType == RaidDetectedTrigger && (s.ServerId == null || s.ServerId == serverId))
            .ToListAsync(ct);

        // Prefer a server-specific override over the "applies to all servers" row.
        return candidates.FirstOrDefault(s => s.ServerId == serverId) ?? candidates.FirstOrDefault(s => s.ServerId == null);
    }

    private static string TierLabel(RaidTier tier) => tier switch
    {
        RaidTier.Tier3 => "Tier 3",
        RaidTier.Tier2 => "Tier 2",
        _ => "Tier 1",
    };
}
