using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rustex.Domain.Abstractions;

namespace Rustex.Infrastructure.Realtime;

/// <summary>One publish that did not fully succeed, waiting to be tried again.
///
/// The payload is kept as a <see cref="JsonElement"/> rather than the original object: it is an
/// immutable snapshot of the value at publish time, so a retry cannot resend state that has since
/// been mutated by the producer that queued it.</summary>
internal sealed record PendingSync(
    LiveScope Scope,
    string Section,
    JsonElement Payload,
    long Version,
    bool NeedsStoreWrite,
    int Attempt,
    DateTimeOffset NotBefore);

/// <summary>Holds retries for publishes that failed. Bounded and drop-oldest: live state is
/// replaced by the next tick anyway, so under sustained failure it is better to lose the stalest
/// update than to grow without limit and take the process down with it.</summary>
public sealed class SyncRetryQueue
{
    internal Channel<PendingSync> Channel { get; } =
        System.Threading.Channels.Channel.CreateBounded<PendingSync>(
            new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest });

    internal bool TryEnqueue(PendingSync item) => Channel.Writer.TryWrite(item);
}

/// <summary>Caches new live state and pushes it to connected clients.
///
/// The two steps are deliberately ordered store-then-broadcast, and treated differently on
/// failure. The store write is what a reconnecting client reads, so it is the one that must
/// eventually land; the broadcast is an optimisation for clients already connected. If the
/// broadcast fails but the store succeeded, clients still converge — just on their next
/// reconnect rather than instantly.</summary>
public sealed class LiveSyncPublisher : ILiveSyncPublisher
{
    private readonly ILiveStateStore _store;
    private readonly ILiveBroadcaster _broadcaster;
    private readonly SyncRetryQueue _retries;
    private readonly ILogger<LiveSyncPublisher> _log;

    public LiveSyncPublisher(
        ILiveStateStore store, ILiveBroadcaster broadcaster, SyncRetryQueue retries, ILogger<LiveSyncPublisher> log)
    {
        _store = store;
        _broadcaster = broadcaster;
        _retries = retries;
        _log = log;
    }

    public async Task PublishAsync(LiveScope scope, string section, object payload, CancellationToken ct)
    {
        var element = JsonSerializer.SerializeToElement(payload);

        long version;
        try
        {
            version = await _store.SetSectionAsync(scope, section, element, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to cache live {Section} for {Scope}; queued for retry", section, scope);
            Enqueue(scope, section, element, version: 0, needsStoreWrite: true, attempt: 1);
            return;
        }

        await BroadcastOrQueueAsync(scope, section, element, version, attempt: 1, ct);
    }

    internal async Task BroadcastOrQueueAsync(
        LiveScope scope, string section, JsonElement payload, long version, int attempt, CancellationToken ct)
    {
        try
        {
            var update = new LiveUpdate(scope.ToString(), section, version, DateTimeOffset.UtcNow, payload);
            await _broadcaster.BroadcastAsync(scope, update, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to broadcast live {Section} for {Scope}; queued for retry", section, scope);
            Enqueue(scope, section, payload, version, needsStoreWrite: false, attempt);
        }
    }

    internal void Enqueue(LiveScope scope, string section, JsonElement payload, long version, bool needsStoreWrite, int attempt)
    {
        if (attempt > SyncRetryWorker.MaxAttempts)
        {
            _log.LogError("Giving up on live {Section} for {Scope} after {Attempts} attempts", section, scope, attempt - 1);
            return;
        }

        _retries.TryEnqueue(new PendingSync(
            scope, section, payload, version, needsStoreWrite, attempt,
            DateTimeOffset.UtcNow + SyncRetryWorker.BackoffFor(attempt)));
    }
}

/// <summary>Drains <see cref="SyncRetryQueue"/> with exponential backoff.
///
/// Runs single-threaded over the channel on purpose: retries exist because a dependency (Redis or
/// the hub) is struggling, and hammering it from many threads is how a brief blip becomes an
/// outage.</summary>
public sealed class SyncRetryWorker : BackgroundService
{
    internal const int MaxAttempts = 5;

    private readonly SyncRetryQueue _queue;
    private readonly ILiveStateStore _store;
    private readonly ILiveSyncPublisher _publisher;
    private readonly ILogger<SyncRetryWorker> _log;

    public SyncRetryWorker(
        SyncRetryQueue queue, ILiveStateStore store, ILiveSyncPublisher publisher, ILogger<SyncRetryWorker> log)
    {
        _queue = queue;
        _store = store;
        _publisher = publisher;
        _log = log;
    }

    /// <summary>1s, 2s, 4s, 8s, 16s. Capped so a long outage does not push a retry hours out,
    /// by which point the state would be worthless anyway.</summary>
    internal static TimeSpan BackoffFor(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, Math.Clamp(attempt - 1, 0, 4)));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var item in _queue.Channel.Reader.ReadAllAsync(ct))
        {
            var wait = item.NotBefore - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, ct); }
                catch (OperationCanceledException) { return; }
            }

            try
            {
                await RetryAsync(item, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Retry worker failed unexpectedly on {Scope}/{Section}", item.Scope, item.Section);
            }
        }
    }

    private async Task RetryAsync(PendingSync item, CancellationToken ct)
    {
        var publisher = (LiveSyncPublisher)_publisher;

        if (item.NeedsStoreWrite)
        {
            long version;
            try
            {
                version = await _store.SetSectionAsync(item.Scope, item.Section, item.Payload, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Retry {Attempt} failed to cache {Scope}/{Section}", item.Attempt, item.Scope, item.Section);
                publisher.Enqueue(item.Scope, item.Section, item.Payload, 0, needsStoreWrite: true, item.Attempt + 1);
                return;
            }

            await publisher.BroadcastOrQueueAsync(item.Scope, item.Section, item.Payload, version, item.Attempt + 1, ct);
            return;
        }

        await publisher.BroadcastOrQueueAsync(item.Scope, item.Section, item.Payload, item.Version, item.Attempt + 1, ct);
    }
}
