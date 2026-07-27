using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;

namespace Rustex.Infrastructure.RustPlus.Fcm;

public sealed class RustPlusFcmClientAdapter : IRustPlusFcmClient
{
    private readonly RustPlusFcm _inner;

    public RustPlusFcmClientAdapter(Credentials credentials, ICollection<string> persistentIds, ILoggerFactory loggerFactory)
    {
        _inner = new RustPlusFcm(credentials, persistentIds, loggerFactory: loggerFactory);
        _inner.OnServerPairing += (_, n) => ServerPairing?.Invoke(n);
        _inner.OnEntityPairing += (_, n) => EntityPairing?.Invoke(n);
        _inner.OnSmartSwitchPairing += (_, n) => SmartSwitchPairing?.Invoke(n);
        _inner.OnSmartAlarmPairing += (_, n) => SmartAlarmPairing?.Invoke(n);
        _inner.OnStorageMonitorPairing += (_, n) => StorageMonitorPairing?.Invoke(n);
        _inner.OnAlarmTriggered += (_, n) => AlarmTriggered?.Invoke(n);
    }

    public event Action<Notification<ServerEvent>>? ServerPairing;
    public event Action<Notification<EntityEvent>>? EntityPairing;
    public event Action<Notification<ulong?>>? SmartSwitchPairing;
    public event Action<Notification<ulong?>>? SmartAlarmPairing;
    public event Action<Notification<ulong?>>? StorageMonitorPairing;
    public event Action<AlarmNotification>? AlarmTriggered;

    public IReadOnlyCollection<string> PersistentIds => _inner.PersistentIds;

    public Task ConnectAsync(CancellationToken ct) => _inner.ConnectAsync(ct);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
