using Rustex.Domain.Entities;

namespace Rustex.Domain.Abstractions;

public interface IWebPushSender
{
    /// <summary>True if the underlying VAPID keys are configured — callers should skip sending
    /// (not throw) when this is false, since Web Push is an optional channel.</summary>
    bool IsConfigured { get; }

    string? PublicKey { get; }

    Task<WebPushResult> SendAsync(PushSubscription subscription, string payloadJson, CancellationToken ct);
}

public enum WebPushResult
{
    Sent,
    /// <summary>The push service returned 404/410 — the subscription is dead and should be deleted.</summary>
    SubscriptionExpired,
    Failed,
}
