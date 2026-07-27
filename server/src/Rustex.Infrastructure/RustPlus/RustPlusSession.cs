using Microsoft.Extensions.Logging;
using Rustex.Infrastructure.RustPlus.Proto;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>Owns one pairing's connection for its whole lifetime: connects, subscribes to
/// broadcasts, and reconnects with backoff whenever the underlying RustPlusClient dies — the
/// bug this replaces was that RustPlusConnectionManager cached a RustPlusClient directly, so one
/// dropped socket permanently 502'd that pairing until someone deleted and re-saved it.</summary>
public sealed class RustPlusSession : IAsyncDisposable
{
    private static readonly int[] BackoffSeconds = [1, 2, 5, 15, 30, 60];

    private readonly Guid _pairingId;
    private readonly ulong _playerId;
    private readonly int _playerToken;
    private readonly string _host;
    private readonly int _port;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _supervisorTask;

    private readonly object _gateLock = new();
    private TaskCompletionSource _readyGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RustPlusClient? _current;

    /// <summary>Fired for every broadcast on this pairing's connection, tagged with the pairing
    /// id so a single manager-level subscriber can fan out to the right consumer.</summary>
    public event Action<Guid, AppBroadcast>? OnBroadcast;

    public RustPlusSession(Guid pairingId, ulong playerId, int playerToken, string host, int port, ILoggerFactory loggerFactory)
    {
        _pairingId = pairingId;
        _playerId = playerId;
        _playerToken = playerToken;
        _host = host;
        _port = port;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<RustPlusSession>();
        _supervisorTask = Task.Run(() => SupervisorLoopAsync(_lifetimeCts.Token));
    }

    /// <summary>Blocks until a healthy client is available or the timeout elapses. If the
    /// session is mid-reconnect, this waits for the *next* successful connect rather than
    /// failing immediately — that's the fix for the dead-socket bug this type replaces.</summary>
    public async Task<RustPlusClient> WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        Task gateTask;
        lock (_gateLock) gateTask = _readyGate.Task;

        if (!gateTask.IsCompleted)
        {
            try
            {
                await gateTask.WaitAsync(timeout, ct);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Rust+ session for pairing {_pairingId} did not become ready within {timeout}.");
            }
        }

        lock (_gateLock)
        {
            if (_current is { IsHealthy: true }) return _current;
        }
        throw new InvalidOperationException("Rust+ session disconnected while waiting to become ready — it will keep retrying in the background.");
    }

    private async Task SupervisorLoopAsync(CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            RustPlusClient? client = null;
            try
            {
                client = new RustPlusClient(_playerId, _playerToken, _loggerFactory.CreateLogger<RustPlusClient>());

                var closedTcs = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.OnClosed += reason => closedTcs.TrySetResult(reason);
                client.OnBroadcast += broadcast => OnBroadcast?.Invoke(_pairingId, broadcast);

                await client.ConnectAsync(_host, _port, ct);

                try
                {
                    await client.SetSubscriptionAsync(true, ct);
                }
                catch (Exception ex)
                {
                    // Not fatal — GetTeamInfo/GetMapMarkers still work by polling. Broadcasts
                    // (team chat, live position updates) just won't arrive until this succeeds.
                    _logger.LogWarning(ex, "setSubscription failed for pairing {PairingId} — broadcasts may not arrive", _pairingId);
                }

                attempt = 0;
                SetReady(client);
                _logger.LogInformation("Rust+ session connected for pairing {PairingId}", _pairingId);

                await closedTcs.Task;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rust+ session for pairing {PairingId} failed to connect (attempt {Attempt})", _pairingId, attempt + 1);
            }
            finally
            {
                SetNotReady();
                if (client is not null) await client.DisposeAsync();
            }

            if (ct.IsCancellationRequested) break;

            var baseDelay = BackoffSeconds[Math.Min(attempt, BackoffSeconds.Length - 1)];
            attempt++;
            var jitter = 1 + (Random.Shared.NextDouble() * 0.4 - 0.2); // ±20%
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(baseDelay * jitter), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void SetReady(RustPlusClient client)
    {
        lock (_gateLock)
        {
            _current = client;
            _readyGate.TrySetResult();
        }
    }

    private void SetNotReady()
    {
        lock (_gateLock)
        {
            _current = null;
            if (_readyGate.Task.IsCompleted)
                _readyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCts.Cancel();
        try { await _supervisorTask; } catch { /* already logged inside the loop */ }

        RustPlusClient? current;
        lock (_gateLock) current = _current;
        if (current is not null) await current.DisposeAsync();

        _lifetimeCts.Dispose();
    }
}
