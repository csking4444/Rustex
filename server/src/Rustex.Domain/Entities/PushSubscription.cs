namespace Rustex.Domain.Entities;

/// <summary>A browser Push API subscription (W3C standard, VAPID-authenticated) — the mechanism
/// that lets EmergencyAlertDispatcher reach a user whose PWA is backgrounded or fully closed,
/// which SignalR (a live-connection-only channel) cannot. One row per browser/device.</summary>
public class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string Endpoint { get; set; } = default!;
    public string P256dhKey { get; set; } = default!;
    public string AuthKey { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
