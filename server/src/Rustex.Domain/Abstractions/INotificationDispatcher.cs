using Rustex.Domain.Entities;

namespace Rustex.Domain.Abstractions;

/// <summary>Saves a Notification row and fans it out across the channels a user has enabled
/// (in-app/SignalR, Discord webhook, Web Push) — the same three channels EmergencyAlertDispatcher
/// already used for raid alerts, pulled out so Shop Alerts / Smart Alarms / Team Tracking don't
/// each reimplement "check settings, save row, broadcast, maybe webhook, maybe push".
///
/// Deliberately does NOT include EmergencyAlertDispatcher's ring-alert/quiet-hours/cooldown
/// logic — that's specific to raid-severity emergency alerts (a full-screen ringing call is
/// wrong UX for "a vending machine restocked"), so EmergencyAlertDispatcher keeps its own path
/// rather than being force-fit through here.</summary>
public interface INotificationDispatcher
{
    Task<Notification> DispatchAsync(DispatchNotificationRequest request, CancellationToken ct);
}

public sealed record DispatchNotificationRequest(
    Guid UserId,
    string Type,
    string Title,
    string Body,
    NotificationSeverity Severity = NotificationSeverity.Info,
    /// <summary>Needed to look up matching Discord webhooks — null skips the Discord channel
    /// even if the user has it enabled.</summary>
    Guid? ServerId = null,
    /// <summary>Matches Webhook.EventTypes, e.g. "ShopAlert", "SmartAlarm".</summary>
    string? WebhookEventType = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null);
