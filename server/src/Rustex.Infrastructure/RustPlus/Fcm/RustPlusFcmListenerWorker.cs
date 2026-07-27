using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Caching;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus;

namespace Rustex.Infrastructure.RustPlus.Fcm;

/// <summary>
/// Holds an FCM push connection per user with Active Rust+ credentials, so pressing "Pair With
/// Server" in game registers automatically instead of requiring manual playerId/playerToken
/// entry. See docs/RUSTPLUS.md for what this depends on and its failure modes.
///
/// Deliberately NOT the blocking-HTTP-request design the old /auto-pair endpoint used (up to two
/// minutes on the request thread). This runs continuously in the background; the web UI polls
/// GET credentials/status to show progress.
/// </summary>
public sealed class RustPlusFcmListenerWorker : BackgroundService
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan OwnershipLockTtl = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan OwnershipRenewInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PersistentIdsFlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpiryWarningWindow = TimeSpan.FromDays(2);
    private static readonly TimeSpan RenotifyCooldown = TimeSpan.FromHours(24);
    private static readonly int[] BackoffSeconds = [5, 15, 60, 300];
    private const int FailuresBeforeReauth = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRedisCacheService _cache;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly RustPlusFcmEventBus _eventBus;
    private readonly RustPlusOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RustPlusFcmListenerWorker> _logger;
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

    private readonly ConcurrentDictionary<Guid, UserSession> _sessions = new();

    public RustPlusFcmListenerWorker(
        IServiceScopeFactory scopeFactory,
        IRedisCacheService cache,
        RustPlusConnectionManager connectionManager,
        RustPlusFcmEventBus eventBus,
        IOptions<RustPlusOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<RustPlusFcmListenerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _connectionManager = connectionManager;
        _eventBus = eventBus;
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableFcmListener)
        {
            _logger.LogInformation("Rust+ FCM listener disabled (RustPlus:EnableFcmListener=false) — manual pairing still works.");
            return;
        }

        _logger.LogInformation("Rust+ FCM listener starting (instance {InstanceId})", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rust+ FCM listener reconcile pass failed");
            }

            try
            {
                await Task.Delay(ReconcileInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }

        foreach (var session in _sessions.Values) await session.DisposeAsync();
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRustPlusCredentialStore>();
        var active = await store.GetAllAsync(RustPlusCredentialStatus.Active, ct);
        var activeIds = active.Select(a => a.UserId).ToHashSet();

        foreach (var (userId, session) in _sessions)
        {
            if (!activeIds.Contains(userId) && _sessions.TryRemove(userId, out var removed))
                await removed.DisposeAsync();
        }

        foreach (var credential in active)
        {
            var age = DateTimeOffset.UtcNow - credential.RegisteredAt;

            if (age >= TimeSpan.FromDays(_options.CredentialLifetimeDays))
            {
                await FlipToNeedsReauthAsync(store, credential.UserId,
                    "Your Rust+ auto-pairing has likely expired (~14-day Steam token limit) — run `rustex-pair` again to restore it. Servers you've already paired keep working regardless.", ct);
                continue;
            }

            if (age >= TimeSpan.FromDays(_options.CredentialLifetimeDays) - ExpiryWarningWindow && ShouldRenotify(credential))
            {
                await NotifyAsync(scope, credential.UserId, "rustplus.expiring_soon", "Rust+ auto-pairing expiring soon",
                    "Re-run `rustex-pair` within the next couple of days to keep automatic pairing working. This won't affect servers you've already paired.", ct);
                await store.MarkNotificationAsync(credential.UserId, ct);
            }

            if (_sessions.ContainsKey(credential.UserId)) continue;

            if (!await _cache.TrySetIfAbsentAsync($"rustex:fcm:owner:{credential.UserId}", _instanceId, OwnershipLockTtl))
                continue; // another replica already owns this user's session

            var session = new UserSession(credential.UserId, this);
            if (_sessions.TryAdd(credential.UserId, session))
                _ = session.RunAsync(ct);
            else
                await session.DisposeAsync();
        }
    }

    private static bool ShouldRenotify(RustPlusAccountCredential credential) =>
        credential.LastNotificationAt is null || credential.LastNotificationAt < DateTimeOffset.UtcNow - RenotifyCooldown;

    private async Task FlipToNeedsReauthAsync(IRustPlusCredentialStore store, Guid userId, string message, CancellationToken ct)
    {
        if (_sessions.TryRemove(userId, out var session)) await session.DisposeAsync();
        await store.RecordFailureAsync(userId, 0, ct); // 0 threshold -> flips immediately regardless of ConsecutiveFailures
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.RustPlusAccountCredentials.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is not null) { row.Status = RustPlusCredentialStatus.NeedsReauth; await db.SaveChangesAsync(ct); }
        await NotifyAsync(scope, userId, "rustplus.needs_reauth", "Rust+ auto-pairing needs reconnecting", message, ct);
    }

    private static async Task NotifyAsync(IServiceScope scope, Guid userId, string type, string title, string body, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Notifications.Add(new Notification { UserId = userId, Type = type, Title = title, Body = body });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>One user's FCM connection lifecycle: connect, dispatch events through a channel
    /// (never touch AppDbContext from the socket thread that raises these events), reconnect
    /// with backoff, and periodically flush PersistentIds so a restart doesn't replay history.</summary>
    private sealed class UserSession : IAsyncDisposable
    {
        private readonly Guid _userId;
        private readonly RustPlusFcmListenerWorker _owner;
        private readonly CancellationTokenSource _lifetimeCts = new();
        private readonly Channel<Func<IServiceScope, CancellationToken, Task>> _channel =
            Channel.CreateUnbounded<Func<IServiceScope, CancellationToken, Task>>();
        private Task? _runTask;
        private Task? _consumerTask;
        private Task? _ownershipRenewalTask;
        private Task? _persistentIdsFlushTask;
        private IRustPlusFcmClient? _client;

        public UserSession(Guid userId, RustPlusFcmListenerWorker owner)
        {
            _userId = userId;
            _owner = owner;
        }

        public Task RunAsync(CancellationToken ct)
        {
            _runTask = ConnectLoopAsync(ct);
            _consumerTask = ConsumeAsync(ct);
            _ownershipRenewalTask = RenewOwnershipLoopAsync(ct);
            _persistentIdsFlushTask = FlushPersistentIdsLoopAsync(ct);
            return _runTask;
        }

        private async Task ConnectLoopAsync(CancellationToken ct)
        {
            var attempt = 0;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);

            while (!linked.Token.IsCancellationRequested)
            {
                try
                {
                    // Scoped strictly to loading credentials — a DI scope (and the DbContext it
                    // hands out) must not stay open for the connection's whole lifetime, which
                    // can be hours.
                    Credentials credentials;
                    List<string> persistentIds;
                    using (var loadScope = _owner._scopeFactory.CreateScope())
                    {
                        var store = loadScope.ServiceProvider.GetRequiredService<IRustPlusCredentialStore>();
                        var loaded = await store.LoadAsync(_userId, linked.Token);
                        if (loaded is null) return; // deleted mid-connect
                        credentials = loaded;
                        persistentIds = await store.LoadPersistentIdsAsync(_userId, linked.Token);
                    }

                    var client = new RustPlusFcmClientAdapter(credentials, persistentIds, _owner._loggerFactory);
                    _client = client;

                    client.ServerPairing += n => Enqueue((s, c) => HandleServerPairingAsync(s, n, c));
                    client.EntityPairing += n => _owner._eventBus.RaiseEntityPairing(_userId, n);
                    client.SmartSwitchPairing += n => _owner._eventBus.RaiseSmartSwitchPairing(_userId, n);
                    client.SmartAlarmPairing += n => _owner._eventBus.RaiseSmartAlarmPairing(_userId, n);
                    client.StorageMonitorPairing += n => _owner._eventBus.RaiseStorageMonitorPairing(_userId, n);
                    client.AlarmTriggered += n => _owner._eventBus.RaiseAlarmTriggered(_userId, n);

                    await client.ConnectAsync(linked.Token);
                    attempt = 0;

                    using (var connectedScope = _owner._scopeFactory.CreateScope())
                    {
                        var connectedStore = connectedScope.ServiceProvider.GetRequiredService<IRustPlusCredentialStore>();
                        await connectedStore.MarkConnectedAsync(_userId, linked.Token);
                    }

                    // RustPlusFcm has no "disconnected" event to await on — it reconnects
                    // internally on transient failures, so we just hold this loop open until
                    // cancelled and let ConnectAsync's own exception surface real failures.
                    await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    attempt++;
                    using (var failScope = _owner._scopeFactory.CreateScope())
                    {
                        var failStore = failScope.ServiceProvider.GetRequiredService<IRustPlusCredentialStore>();
                        await failStore.RecordFailureAsync(_userId, FailuresBeforeReauth, CancellationToken.None);
                    }
                    _owner._logger.LogWarning(ex, "Rust+ FCM connection failed for user {UserId} (attempt {Attempt})", _userId, attempt);

                    var delay = BackoffSeconds[Math.Min(attempt - 1, BackoffSeconds.Length - 1)];
                    try { await Task.Delay(TimeSpan.FromSeconds(delay), linked.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private void Enqueue(Func<IServiceScope, CancellationToken, Task> work) => _channel.Writer.TryWrite(work);

        private async Task ConsumeAsync(CancellationToken ct)
        {
            await foreach (var work in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    using var scope = _owner._scopeFactory.CreateScope();
                    await work(scope, ct);
                }
                catch (Exception ex)
                {
                    _owner._logger.LogWarning(ex, "Rust+ FCM event handler failed for user {UserId}", _userId);
                }
            }
        }

        private async Task HandleServerPairingAsync(IServiceScope scope, Notification<ServerEvent> notification, CancellationToken ct)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var encryption = scope.ServiceProvider.GetService<Security.IEncryptionService>();
            if (encryption is null) return;

            var handler = new RustServerPairingHandler(db, encryption, _owner._connectionManager);
            await handler.HandleAsync(_userId, notification, ct);
        }

        private async Task RenewOwnershipLoopAsync(CancellationToken ct)
        {
            try
            {
                while (await WaitAsync(OwnershipRenewInterval, ct))
                    await _owner._cache.SetAsync($"rustex:fcm:owner:{_userId}", _owner._instanceId, OwnershipLockTtl);
            }
            catch (OperationCanceledException) { }
        }

        private async Task FlushPersistentIdsLoopAsync(CancellationToken ct)
        {
            try
            {
                while (await WaitAsync(PersistentIdsFlushInterval, ct))
                {
                    if (_client is null) continue;
                    using var scope = _owner._scopeFactory.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IRustPlusCredentialStore>();
                    await store.SavePersistentIdsAsync(_userId, _client.PersistentIds, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task<bool> WaitAsync(TimeSpan interval, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
            try
            {
                await Task.Delay(interval, linked.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _lifetimeCts.Cancel();
            _channel.Writer.TryComplete();

            foreach (var t in new[] { _runTask, _consumerTask, _ownershipRenewalTask, _persistentIdsFlushTask })
            {
                if (t is null) continue;
                try { await t; } catch { /* already logged inside the loop */ }
            }

            if (_client is not null) await _client.DisposeAsync();
            await _owner._cache.RemoveAsync($"rustex:fcm:owner:{_userId}");
            _lifetimeCts.Dispose();
        }
    }
}
