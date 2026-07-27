using System.Collections.Concurrent;
using System.Net.WebSockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Rustex.Infrastructure.RustPlus.Proto;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>
/// A live connection to one paired Rust server's Rust+ WebSocket endpoint. Requires an already
/// obtained (playerId, playerToken) pair — see docs/RUSTPLUS.md for how pairing happens (manual
/// entry, or the `rustex-pair` one-time setup once that exists).
///
/// Wire format: each WebSocket binary frame is exactly one serialized AppRequest (outbound) or
/// AppMessage (inbound) protobuf message — no additional framing. Requests are correlated to
/// responses via the `seq` field, which this client assigns and tracks.
/// </summary>
public sealed class RustPlusClient : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);
    private const int HeartbeatFailuresBeforeFaulted = 2;

    private readonly ClientWebSocket _socket = new();
    private readonly ulong _playerId;
    private readonly int _playerToken;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<AppResponse>> _pending = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    // ClientWebSocket.SendAsync throws if two calls overlap, and this client is shared across
    // concurrent HTTP requests via RustPlusConnectionManager — without this lock, two callers
    // hitting the same pairing at once throw "There is already one outstanding SendAsync call".
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _nextSeq;
    private Task? _receiveLoop;
    private Task? _heartbeatLoop;
    private volatile bool _faulted;

    /// <summary>Raised for server-pushed events the client didn't explicitly request — e.g. a
    /// team member's status changing, or a new team chat message.</summary>
    public event Action<AppBroadcast>? OnBroadcast;

    /// <summary>Raised once, when the connection ends for any reason (clean close, network
    /// failure, heartbeat timeout). The owner (RustPlusSession) uses this to trigger a
    /// reconnect — this client itself never reconnects.</summary>
    public event Action<Exception?>? OnClosed;

    public bool IsHealthy => _socket.State == WebSocketState.Open && !_faulted;

    public RustPlusClient(ulong playerId, int playerToken, ILogger logger)
    {
        _playerId = playerId;
        _playerToken = playerToken;
        _logger = logger;
    }

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        using var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectTimeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        var uri = new Uri($"ws://{host}:{port}");
        await _socket.ConnectAsync(uri, connectTimeoutCts.Token);

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_lifetimeCts.Token), CancellationToken.None);
        _heartbeatLoop = Task.Run(() => HeartbeatLoopAsync(_lifetimeCts.Token), CancellationToken.None);
    }

    public async Task<AppInfo> GetInfoAsync(CancellationToken ct)
    {
        var response = await SendAsync(new AppRequest { GetInfo = new AppEmpty() }, ct);
        return response.Info ?? throw ErrorOrUnexpected(response);
    }

    public async Task<AppTeamInfo> GetTeamInfoAsync(CancellationToken ct)
    {
        var response = await SendAsync(new AppRequest { GetTeamInfo = new AppEmpty() }, ct);
        return response.TeamInfo ?? throw ErrorOrUnexpected(response);
    }

    public async Task<IReadOnlyList<AppMarker>> GetMapMarkersAsync(CancellationToken ct)
    {
        var response = await SendAsync(new AppRequest { GetMapMarkers = new AppEmpty() }, ct);
        return response.MapMarkers?.Markers ?? throw ErrorOrUnexpected(response);
    }

    public async Task<AppEntityInfo> GetEntityInfoAsync(uint entityId, CancellationToken ct)
    {
        var response = await SendAsync(new AppRequest { EntityId = entityId, GetEntityInfo = new AppEmpty() }, ct);
        return response.EntityInfo ?? throw ErrorOrUnexpected(response);
    }

    public async Task SetEntityValueAsync(uint entityId, bool value, CancellationToken ct)
    {
        await SendAsync(new AppRequest { EntityId = entityId, SetEntityValue = new AppSetEntityValue { Value = value } }, ct);
    }

    public async Task SendTeamMessageAsync(string message, CancellationToken ct)
    {
        await SendAsync(new AppRequest { SendTeamMessage = new AppSendMessage { Message = message } }, ct);
    }

    /// <summary>Subscribes this connection to server-pushed broadcasts (team changes, chat,
    /// entity changes). Rust+ does not enable these by default — call this once per connection,
    /// which is why RustPlusSession sends it right after every (re)connect.</summary>
    public async Task SetSubscriptionAsync(bool value, CancellationToken ct)
    {
        await SendAsync(new AppRequest { SetSubscription = new AppFlag { Value = value } }, ct);
    }

    private static Exception ErrorOrUnexpected(AppResponse response) =>
        new InvalidOperationException(response.Error?.Error ?? "Rust+ server returned an unexpected empty response.");

    private async Task<AppResponse> SendAsync(AppRequest request, CancellationToken ct)
    {
        if (!IsHealthy)
            throw new InvalidOperationException("Not connected to a Rust+ server.");

        var seq = (uint)Interlocked.Increment(ref _nextSeq);
        request.Seq = seq;
        request.PlayerId = _playerId;
        request.PlayerToken = _playerToken;

        var tcs = new TaskCompletionSource<AppResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = tcs;

        try
        {
            var bytes = request.ToByteArray();

            await _sendLock.WaitAsync(ct);
            try
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Binary, true, ct);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
            timeoutCts.CancelAfter(RequestTimeout);
            await using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _pending.TryRemove(seq, out _);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        // WebSocket-level ping/pong (KeepAliveInterval above) doesn't reliably keep a Rust+
        // socket alive on its own — this issues a cheap real request instead. Two consecutive
        // failures mark the client faulted so SendAsync fails fast rather than callers hanging
        // on a socket that looks Open but is actually dead.
        var consecutiveFailures = 0;

        try
        {
            using var timer = new PeriodicTimer(HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_faulted) return;

                try
                {
                    await SendAsync(new AppRequest { GetTime = new AppEmpty() }, ct);
                    consecutiveFailures = 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    consecutiveFailures++;
                    _logger.LogWarning(ex, "Rust+ heartbeat failed ({Count}/{Max})", consecutiveFailures, HeartbeatFailuresBeforeFaulted);
                    if (consecutiveFailures >= HeartbeatFailuresBeforeFaulted)
                    {
                        FaultAndClose(ex);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        Exception? closeReason = null;

        try
        {
            while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                messageStream.Position = 0;

                AppMessage message;
                try
                {
                    message = AppMessage.Parser.ParseFrom(messageStream);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse an incoming Rust+ message — dropping it");
                    continue;
                }

                if (message.Response is not null && _pending.TryGetValue(message.Response.Seq, out var tcs))
                    tcs.TrySetResult(message.Response);
                else if (message.Broadcast is not null)
                    OnBroadcast?.Invoke(message.Broadcast);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            closeReason = ex;
            _logger.LogWarning(ex, "Rust+ WebSocket receive loop ended unexpectedly");
        }
        finally
        {
            FaultAndClose(closeReason);
        }
    }

    /// <summary>Marks the client dead and fails every in-flight request immediately, instead of
    /// letting each one hang until its own 10-second timeout. Idempotent — safe to call from
    /// both the receive loop and the heartbeat loop racing each other.</summary>
    private void FaultAndClose(Exception? reason)
    {
        if (_faulted) return;
        _faulted = true;

        foreach (var (seq, tcs) in _pending)
        {
            tcs.TrySetException(reason ?? new IOException("Rust+ connection closed."));
            _pending.TryRemove(seq, out _);
        }

        OnClosed?.Invoke(reason);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCts.Cancel();

        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            }
            catch
            {
                // best-effort close
            }
        }

        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; } catch { /* already logged inside the loop */ }
        }
        if (_heartbeatLoop is not null)
        {
            try { await _heartbeatLoop; } catch { /* already logged inside the loop */ }
        }

        _socket.Dispose();
        _lifetimeCts.Dispose();
        _sendLock.Dispose();
    }
}
