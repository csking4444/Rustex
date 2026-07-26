using Rustex.Domain.Abstractions;

namespace Rustex.Infrastructure.EventIngestion;

/// <summary>
/// Stub — see IRustPlusNotificationListener for the full explanation of what's missing. In
/// short: receiving Smart Alarm pushes requires reproducing Rust+'s FCM registration handshake
/// (Steam OpenID -> Expo/FCM push token -> pairing service -> persistent GCM listener), which
/// needs a live Steam/Rust+ account to build and test against. This throws rather than silently
/// returning an empty stream so a caller that enables it gets a clear signal instead of "raid
/// alarms just don't work and nothing says why".
/// </summary>
public class RustPlusNotificationListener : IRustPlusNotificationListener
{
    public IAsyncEnumerable<RustPlusSmartAlarmNotification> ListenAsync(Guid userId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "RustPlusNotificationListener is not yet wired to a live FCM/Smart Alarm pairing session. " +
            "See IRustPlusNotificationListener's doc comment for what's required to implement this for real. " +
            "SimulatedEventSource + EventIngestionWorker already exercise the same notification-count-to-tier " +
            "pipeline this would feed, so tiering/cooldown/dispatch logic can be built and tested ahead of it.");
    }
}
