namespace Rustex.Domain.Abstractions;

/// <summary>
/// Listens for push notifications Facepunch's Rust+ app itself receives — most importantly
/// in-game Smart Alarms, which fire a push notification (via FCM) when the alarm entity takes
/// damage nearby. This is the one piece of "raid" signal Rust+ genuinely provides without a
/// server plugin: a player places a Smart Alarm at their base, pairs it to their Rust+ account,
/// and gets pinged when it's tripped.
///
/// Wiring this up for real requires reproducing the (undocumented by Facepunch, but publicly
/// reverse-engineered by the community — see liamcottle/rustplus.js) FCM registration handshake:
/// exchange a Steam OpenID session for an Expo/FCM push token, register it with Rust+'s pairing
/// service, then keep a persistent GCM/FCM listener connection alive per user. That's a
/// meaningfully different integration than the request/response Steam API calls used elsewhere
/// in this project, and needs a live account to build against — see RustPlusNotificationListener
/// for exactly where that work picks up.
///
/// Once received, a Smart Alarm ping is translated into a RaidCandidateEvent (EventType =
/// "smart_alarm") and fed into the same RaidAlarmEvaluator pipeline as any other event source —
/// "amount of notifications" is literally the explosion-count clustering input for these.
/// </summary>
public interface IRustPlusNotificationListener
{
    /// <summary>Starts listening for this user's paired Smart Alarm notifications. The returned
    /// stream stays open until cancelled or the underlying push connection drops (callers should
    /// treat a completed enumeration as "reconnect", not "done forever").</summary>
    IAsyncEnumerable<RustPlusSmartAlarmNotification> ListenAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record RustPlusSmartAlarmNotification(
    Guid UserId,
    string PairedServerAddress, // ip:port of the Rust+ paired server, used to resolve which RustServer this belongs to
    string AlarmName,
    string Message,
    DateTimeOffset ReceivedAt);
