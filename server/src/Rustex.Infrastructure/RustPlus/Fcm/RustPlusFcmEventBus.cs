using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace Rustex.Infrastructure.RustPlus.Fcm;

/// <summary>Decouples RustPlusFcmListenerWorker (which owns the FCM connections and handles
/// server pairing itself, since that entity already exists) from the features that don't exist
/// yet — Smart Devices needs entity-pairing/alarm events, and Phase 6 subscribes to this rather
/// than the worker needing to know about Smart Devices.</summary>
public sealed class RustPlusFcmEventBus
{
    public event Action<Guid, Notification<EntityEvent>>? EntityPairing;
    public event Action<Guid, Notification<ulong?>>? SmartSwitchPairing;
    public event Action<Guid, Notification<ulong?>>? SmartAlarmPairing;
    public event Action<Guid, Notification<ulong?>>? StorageMonitorPairing;
    public event Action<Guid, AlarmNotification>? AlarmTriggered;

    internal void RaiseEntityPairing(Guid userId, Notification<EntityEvent> n) => EntityPairing?.Invoke(userId, n);
    internal void RaiseSmartSwitchPairing(Guid userId, Notification<ulong?> n) => SmartSwitchPairing?.Invoke(userId, n);
    internal void RaiseSmartAlarmPairing(Guid userId, Notification<ulong?> n) => SmartAlarmPairing?.Invoke(userId, n);
    internal void RaiseStorageMonitorPairing(Guid userId, Notification<ulong?> n) => StorageMonitorPairing?.Invoke(userId, n);
    internal void RaiseAlarmTriggered(Guid userId, AlarmNotification n) => AlarmTriggered?.Invoke(userId, n);
}
