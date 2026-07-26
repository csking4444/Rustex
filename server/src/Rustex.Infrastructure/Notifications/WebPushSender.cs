using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rustex.Domain.Abstractions;
using WebPush;
using DomainPushSubscription = Rustex.Domain.Entities.PushSubscription;

namespace Rustex.Infrastructure.Notifications;

public class WebPushSender : IWebPushSender
{
    private readonly WebPushOptions _options;
    private readonly WebPushClient _client = new();
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(IOptions<WebPushOptions> options, ILogger<WebPushSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.PublicKey) && !string.IsNullOrWhiteSpace(_options.PrivateKey);

    public string? PublicKey => _options.PublicKey;

    public async Task<WebPushResult> SendAsync(DomainPushSubscription subscription, string payloadJson, CancellationToken ct)
    {
        if (!IsConfigured) return WebPushResult.Failed;

        var vapidDetails = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
        var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dhKey, subscription.AuthKey);

        try
        {
            // Not passing `ct` through: the WebPush package's SendNotificationAsync overload
            // set differs enough across versions that pinning to a cancellation-token overload
            // here risks a version mismatch — this call is short-lived (a single HTTP POST) so
            // the omission is low-cost.
            await _client.SendNotificationAsync(pushSubscription, payloadJson, vapidDetails);
            return WebPushResult.Sent;
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Push subscription {Endpoint} is no longer valid ({Status})", subscription.Endpoint, ex.StatusCode);
            return WebPushResult.SubscriptionExpired;
        }
        catch (Exception ex)
        {
            // Web Push is a best-effort channel — a failure here must never block the other
            // notification channels in EmergencyAlertDispatcher.
            _logger.LogWarning(ex, "Web Push send to {Endpoint} failed", subscription.Endpoint);
            return WebPushResult.Failed;
        }
    }
}
