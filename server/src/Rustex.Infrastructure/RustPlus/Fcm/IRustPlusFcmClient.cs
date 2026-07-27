using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace Rustex.Infrastructure.RustPlus.Fcm;

/// <summary>Thin wrapper over the vendor RustPlusFcm class, existing purely so
/// RustPlusFcmListenerWorker can be driven by a fake in tests instead of a live FCM connection
/// (which cannot be established offline — see docs/RUSTPLUS.md).</summary>
public interface IRustPlusFcmClient : IAsyncDisposable
{
    event Action<Notification<ServerEvent>>? ServerPairing;
    event Action<Notification<EntityEvent>>? EntityPairing;
    event Action<Notification<ulong?>>? SmartSwitchPairing;
    event Action<Notification<ulong?>>? SmartAlarmPairing;
    event Action<Notification<ulong?>>? StorageMonitorPairing;
    event Action<AlarmNotification>? AlarmTriggered;

    /// <summary>Message ids already delivered on this connection — grows as notifications
    /// arrive; the caller should periodically persist it so a restart doesn't replay history.</summary>
    IReadOnlyCollection<string> PersistentIds { get; }

    Task ConnectAsync(CancellationToken ct);
}

public delegate IRustPlusFcmClient RustPlusFcmClientFactory(Credentials credentials, ICollection<string> persistentIds);
