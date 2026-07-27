using Microsoft.EntityFrameworkCore;
using Rustex.Domain;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Infrastructure.Notifications;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly AppDbContext _db;
    private readonly IRaidEventBroadcaster _broadcaster;
    private readonly IDiscordWebhookSender _discord;
    private readonly IWebPushSender _webPush;

    public NotificationDispatcher(AppDbContext db, IRaidEventBroadcaster broadcaster, IDiscordWebhookSender discord, IWebPushSender webPush)
    {
        _db = db;
        _broadcaster = broadcaster;
        _discord = discord;
        _webPush = webPush;
    }

    public async Task<Notification> DispatchAsync(DispatchNotificationRequest request, CancellationToken ct)
    {
        var notification = new Notification
        {
            UserId = request.UserId,
            Type = request.Type,
            Title = request.Title,
            Body = request.Body,
            Severity = request.Severity,
            RelatedEntityType = request.RelatedEntityType,
            RelatedEntityId = request.RelatedEntityId,
        };
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == request.UserId, ct);

        if (settings?.DesktopEnabled ?? true)
        {
            await _broadcaster.BroadcastNotificationCreatedAsync(request.UserId, new
            {
                notification.Id,
                notification.Type,
                notification.Title,
                notification.Body,
                notification.Severity,
                notification.CreatedAt,
            }, ct);
        }

        if ((settings?.DiscordEnabled ?? false) && request.ServerId is not null && request.WebhookEventType is not null)
        {
            var webhooks = await _db.Webhooks
                .Where(w => w.ServerId == request.ServerId && w.IsActive && w.EventTypes.Contains(request.WebhookEventType))
                .ToListAsync(ct);

            var color = request.Severity switch
            {
                NotificationSeverity.Critical => 0xB3261E,
                NotificationSeverity.Warning => 0xF59E0B,
                _ => 0xD97745,
            };

            foreach (var webhook in webhooks)
                await _discord.SendEmbedAsync(webhook.Url, request.Title, request.Body, color, ct);
        }

        if ((settings?.PushEnabled ?? false) && _webPush.IsConfigured)
            await SendWebPushAsync(request.UserId, notification, ct);

        return notification;
    }

    /// <summary>Mirrors EmergencyAlertDispatcher.SendWebPushAsync — fires regardless of live
    /// SignalR connection state, since that's the entire point of the Push API, and deletes a
    /// subscription the push service reports as gone rather than retrying it forever.</summary>
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
            var result = await _webPush.SendAsync(subscription, payloadJson, ct);
            if (result == WebPushResult.SubscriptionExpired)
                _db.PushSubscriptions.Remove(subscription);
        }

        await _db.SaveChangesAsync(ct);
    }
}
