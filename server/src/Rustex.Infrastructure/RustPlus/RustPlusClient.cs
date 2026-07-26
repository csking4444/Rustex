using System.Collections.Concurrent;
using System.Net.WebSockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Rustex.Infrastructure.RustPlus.Proto;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>
/// A live connection to one paired Rust server's Rust+ WebSocket endpoint. Requires an already
/// obtained (playerId, playerToken) pair from pairing — see docs/ARCHITECTURE.md for how that
/// pairing normally happens (Stage B: FCM push notifications) versus the manual-entry path this
/// client works with regardless of how the token was obtained.
///
/// Wire format: each WebSocket binary frame is exactly one serialized AppRequest (outbound) or
/// AppMessage (inbound) protobuf message — no additional framing. Requests are correlated to
/// responses via the `seq` field, which this client assigns and tracks.
/// </summary>
public sealed class RustPlusClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ulong _playerId;
    private readonly uint _playerToken;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<AppResponse>> _pending = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private int _nextSeq;
    private Task? _receiveLoop;

    /// <summary>Raised for server-pushed events the client didn't explicitly request — e.g. a
    /// team member's status changing, or a new team chat message.</summary>
    public event Action<AppBroadcast>? OnBroadcast;

    public RustPlusClient(ulong playerId, uint playerToken, ILogger logger)
    {
        _playerId = playerId;
        _playerToken = playerToken;
        _logger = logger;
    }

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        var uri = new Uri($"ws://{host}:{port}");
        await _socket.ConnectAsync(uri, ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_lifetimeCts.Token), CancellationToken.None);
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

    private static Exception ErrorOrUnexpected(AppResponse response) =>
        new InvalidOperationException(response.Error?.Error ?? "Rust+ server returned an unexpected empty response.");

    private async Task<AppResponse> SendAsync(AppRequest request, CancellationToken ct)
    {
        if (_socket.State != WebSocketState.Open)
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
            await _socket.SendAsync(bytes, WebSocketMessageType.Binary, true, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
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

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];

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
            _logger.LogWarning(ex, "Rust+ WebSocket receive loop ended unexpectedly");
        }
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

        _socket.Dispose();
        _lifetimeCts.Dispose();
    }
}
