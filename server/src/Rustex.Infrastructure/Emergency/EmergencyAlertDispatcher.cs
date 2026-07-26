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
    private readonly ILogger<EmergencyAlertDispatcher> _logger;

    public EmergencyAlertDispatcher(
        AppDbContext db,
        IClientConnectionRegistry connectionRegistry,
        IRaidEventBroadcaster broadcaster,
        ILogger<EmergencyAlertDispatcher> logger)
    {
        _db = db;
        _connectionRegistry = connectionRegistry;
        _broadcaster = broadcaster;
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

        var activeKinds = _connectionRegistry.GetActiveKinds(userId);
        if (activeKinds.Contains(ClientKind.App))
            await _broadcaster.BroadcastIncomingRaidCallAsync(userId, payload, ct);
        else
            await _broadcaster.BroadcastRaidAlertNotificationAsync(userId, payload, ct);

        // TODO(Phase 5): also fire a Web Push (service worker) delivery for App-kind users who
        // are currently disconnected/backgrounded — SignalR only reaches live connections.
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
