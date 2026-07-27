using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using Rustex.Domain;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus.Fcm;
using Rustex.Infrastructure.RustPlus.Proto;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>
/// Populates RustPlusSmartDevice from FCM entity-pairing pushes, keeps LastKnownValue in sync
/// from live entityChanged broadcasts, and raises a RaidEvent (Source=RustPlus) the moment a
/// paired Smart Alarm trips — the one genuine raid signal Rust+ can supply without a server
/// plugin. Two independent channels can report the same trip (the entityChanged broadcast, which
/// only arrives while a session socket is connected and subscribed, and the OnAlarmTriggered FCM
/// push, which arrives even when nothing is connected) — both funnel through the same per-server
/// 10s dedupe window rather than double-firing. AlarmNotification doesn't carry an entityId (only
/// ServerId/Title/Message), so the dedupe key is per-server rather than per-device; a real base
/// with two alarms tripping within 10s of each other would only raise one RaidEvent, which is an
/// acceptable trade — the raid alarm pipeline already clusters by time window anyway.
/// </summary>
public sealed class RustPlusSmartDevicesWorker : BackgroundService
{
    private static readonly TimeSpan AlarmDedupeWindow = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly RustPlusFcmEventBus _eventBus;
    private readonly ILogger<RustPlusSmartDevicesWorker> _logger;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _recentAlarmEvents = new();
    private readonly Channel<Func<IServiceScope, CancellationToken, Task>> _channel =
        Channel.CreateUnbounded<Func<IServiceScope, CancellationToken, Task>>();

    public RustPlusSmartDevicesWorker(
        IServiceScopeFactory scopeFactory, RustPlusConnectionManager connectionManager,
        RustPlusFcmEventBus eventBus, ILogger<RustPlusSmartDevicesWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _eventBus.EntityPairing += OnEntityPairing;
        _eventBus.SmartSwitchPairing += (userId, n) => OnTypedPairing(userId, n, SmartDeviceKind.Switch);
        _eventBus.SmartAlarmPairing += (userId, n) => OnTypedPairing(userId, n, SmartDeviceKind.Alarm);
        _eventBus.StorageMonitorPairing += (userId, n) => OnTypedPairing(userId, n, SmartDeviceKind.StorageMonitor);
        _eventBus.AlarmTriggered += OnAlarmTriggered;
        _connectionManager.OnBroadcast += OnBroadcast;

        stoppingToken.Register(() =>
        {
            _eventBus.EntityPairing -= OnEntityPairing;
            _eventBus.AlarmTriggered -= OnAlarmTriggered;
            _connectionManager.OnBroadcast -= OnBroadcast;
        });

        return ConsumeAsync(stoppingToken);
    }

    private void Enqueue(Func<IServiceScope, CancellationToken, Task> work) => _channel.Writer.TryWrite(work);

    private void OnEntityPairing(Guid userId, Notification<EntityEvent> n) =>
        Enqueue((scope, ct) => HandleEntityPairingAsync(scope, userId, n, ct));

    private void OnTypedPairing(Guid userId, Notification<ulong?> n, SmartDeviceKind kind) =>
        Enqueue((scope, ct) => HandleTypedPairingAsync(scope, userId, n, kind, ct));

    private void OnAlarmTriggered(Guid userId, AlarmNotification n) =>
        Enqueue((scope, ct) => HandleAlarmTriggeredAsync(scope, userId, n, ct));

    private void OnBroadcast(Guid pairingId, AppBroadcast broadcast)
    {
        if (broadcast.EntityChanged is { } change)
            Enqueue((scope, ct) => HandleEntityChangedAsync(scope, pairingId, change, ct));
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var work in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await work(scope, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RustPlusSmartDevicesWorker failed to process an event");
            }
        }
    }

    private static async Task HandleEntityPairingAsync(IServiceScope scope, Guid userId, Notification<EntityEvent> n, CancellationToken ct)
    {
        if (n.Data.EntityId is not { } entityId || ToKind(n.Data.EntityType) is not { } kind) return;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var server = await db.RustServers.FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.FacepunchServerId == n.ServerId, ct);
        if (server is null) return; // pairing push arrived before the server-pairing push — will resolve on the next entityChanged

        await UpsertDeviceAsync(db, userId, server.Id, (long)entityId, kind, n.Data.EntityName, ct);
    }

    private static async Task HandleTypedPairingAsync(IServiceScope scope, Guid userId, Notification<ulong?> n, SmartDeviceKind kind, CancellationToken ct)
    {
        if (n.Data is not { } entityId) return;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var server = await db.RustServers.FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.FacepunchServerId == n.ServerId, ct);
        if (server is null) return;

        await UpsertDeviceAsync(db, userId, server.Id, (long)entityId, kind, null, ct);
    }

    private static async Task UpsertDeviceAsync(AppDbContext db, Guid userId, Guid serverId, long entityId, SmartDeviceKind kind, string? name, CancellationToken ct)
    {
        var device = await db.RustPlusSmartDevices.FirstOrDefaultAsync(d => d.ServerId == serverId && d.EntityId == entityId, ct);
        var isNew = device is null;
        if (device is null)
        {
            device = new RustPlusSmartDevice { UserId = userId, ServerId = serverId, EntityId = entityId, Type = kind };
            db.RustPlusSmartDevices.Add(device);
        }

        if (!string.IsNullOrWhiteSpace(name)) device.Name = name;
        else if (isNew) device.Name = $"{kind} #{entityId}";

        if (isNew)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = "rustplus.device_paired",
                Title = $"Paired a new {kind}",
                Body = device.Name,
                RelatedEntityType = nameof(RustPlusSmartDevice),
                RelatedEntityId = device.Id,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task HandleAlarmTriggeredAsync(IServiceScope scope, Guid userId, AlarmNotification n, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var server = await db.RustServers.FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.FacepunchServerId == n.ServerId, ct);
        if (server is null) return;

        if (!ShouldRaise(server.Id)) return;

        await RaiseRaidEventAsync(scope, server.Id, n.Title, n.Message, ct);
    }

    private async Task HandleEntityChangedAsync(IServiceScope scope, Guid pairingId, AppEntityChanged change, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pairing = await db.RustPlusPairings.FirstOrDefaultAsync(p => p.Id == pairingId, ct);
        if (pairing is null) return;

        var device = await db.RustPlusSmartDevices.FirstOrDefaultAsync(d => d.ServerId == pairing.ServerId && d.EntityId == change.EntityId, ct);
        if (device is null) return; // not a device we've paired/recorded yet

        var wasTriggered = device.LastKnownValue == true;

        device.LastKnownValue = change.Payload?.Value;
        device.LastKnownCapacity = change.Payload?.Capacity;
        device.LastKnownItemsJson = change.Payload is { Items.Count: > 0 } payload
            ? JsonSerializer.Serialize(payload.Items.Select(i => new { i.ItemId, i.Quantity, i.ItemIsBlueprint }))
            : null;
        device.LastChangedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var justTriggered = device.Type == SmartDeviceKind.Alarm && device.AlarmRaisesRaidEvent && !wasTriggered && device.LastKnownValue == true;
        if (justTriggered && ShouldRaise(pairing.ServerId))
            await RaiseRaidEventAsync(scope, pairing.ServerId, device.Name, "Triggered via live entity broadcast.", ct);
    }

    private bool ShouldRaise(Guid serverId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_recentAlarmEvents.TryGetValue(serverId, out var last) && now - last < AlarmDedupeWindow)
            return false;
        _recentAlarmEvents[serverId] = now;
        return true;
    }

    private static async Task RaiseRaidEventAsync(IServiceScope scope, Guid serverId, string? alarmName, string? message, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var raidEvent = new RaidEvent
        {
            ServerId = serverId,
            Tier = RaidTier.Tier1,
            RaidType = "Smart Alarm",
            ExplosionCount = 1,
            EstimatedSize = "Unknown",
            Status = RaidStatus.Active,
            Source = EventSourceKind.RustPlus,
            MetadataJson = JsonSerializer.Serialize(new { alarm = alarmName, message }),
        };
        db.RaidEvents.Add(raidEvent);
        await db.SaveChangesAsync(ct);

        var broadcaster = scope.ServiceProvider.GetService<IRaidEventBroadcaster>();
        if (broadcaster is not null)
        {
            await broadcaster.BroadcastRaidEventCreatedAsync(serverId, new
            {
                raidEvent.Id,
                raidEvent.ServerId,
                raidEvent.DetectedAt,
                raidEvent.Tier,
                raidEvent.RaidType,
                raidEvent.Source,
            }, ct);
        }

        var dispatcher = scope.ServiceProvider.GetService<IEmergencyAlertDispatcher>();
        if (dispatcher is not null) await dispatcher.DispatchAsync(raidEvent, ct);
    }

    private static SmartDeviceKind? ToKind(EntityType? type) => type switch
    {
        EntityType.Switch => SmartDeviceKind.Switch,
        EntityType.Alarm => SmartDeviceKind.Alarm,
        EntityType.StorageMonitor => SmartDeviceKind.StorageMonitor,
        _ => null,
    };
}
