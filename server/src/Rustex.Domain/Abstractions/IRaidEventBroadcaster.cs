namespace Rustex.Domain.Abstractions;

/// <summary>Pushes real-time updates to connected clients. Implemented over SignalR in the Api
/// project — kept as an interface here so Infrastructure's background workers don't need a
/// reference to the web layer.</summary>
public interface IRaidEventBroadcaster
{
    Task BroadcastRaidEventCreatedAsync(Guid serverId, object payload, CancellationToken ct);
    Task BroadcastServerStatusUpdatedAsync(Guid serverId, object payload, CancellationToken ct);

    /// <summary>Full-screen, looping-audio "incoming call" style alert — sent only to a user's
    /// connections registered as ClientKind.App (installed/standalone). Not a real telephony
    /// call: a browser/PWA can't register with iOS/Android's native call stack, so this is the
    /// closest equivalent achievable without a native app shell. See docs/ARCHITECTURE.md.</summary>
    Task BroadcastIncomingRaidCallAsync(Guid userId, object payload, CancellationToken ct);

    /// <summary>Plain notification-style alert for ClientKind.Desktop connections — rendered as
    /// a browser Notification, no ringing/audio escalation.</summary>
    Task BroadcastRaidAlertNotificationAsync(Guid userId, object payload, CancellationToken ct);
}
