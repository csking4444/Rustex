using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.RustPlus.Proto;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>Keeps one RustPlusSession per pairing, connecting lazily on first use and
/// reconnecting automatically for as long as the session lives. Registered as a singleton so
/// sessions outlive individual HTTP requests — a Rust+ WebSocket is meant to be a long-lived
/// connection, not reconnected per API call.</summary>
public class RustPlusConnectionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, RustPlusSession> _sessions = new();
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Fires for every broadcast on every active session, tagged with the pairing id —
    /// this is how Team Tracking, Chat Assistant, and Smart Devices consume live pushes instead
    /// of the previous behaviour where OnBroadcast had zero subscribers anywhere in the app.</summary>
    public event Action<Guid, AppBroadcast>? OnBroadcast;

    public RustPlusConnectionManager(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public Task<RustPlusClient> GetOrConnectAsync(RustPlusPairing pairing, int playerToken, CancellationToken ct)
    {
        var session = GetOrCreateSession(pairing, playerToken);
        return session.WaitReadyAsync(TimeSpan.FromSeconds(15), ct);
    }

    /// <summary>Opens (or reuses) a session without waiting for it to connect — used by the
    /// startup warmup worker and by anything that just wants broadcasts flowing without making
    /// a request of its own.</summary>
    public void EnsureSession(RustPlusPairing pairing, int playerToken) => GetOrCreateSession(pairing, playerToken);

    public IReadOnlyCollection<Guid> ActiveSessionIds => _sessions.Keys.ToArray();

    private RustPlusSession GetOrCreateSession(RustPlusPairing pairing, int playerToken)
    {
        if (_sessions.TryGetValue(pairing.Id, out var existing)) return existing;

        var candidate = new RustPlusSession(pairing.Id, pairing.PlayerId, playerToken, pairing.ServerIp, pairing.ServerPort, _loggerFactory);
        candidate.OnBroadcast += (pairingId, broadcast) => OnBroadcast?.Invoke(pairingId, broadcast);

        var winner = _sessions.GetOrAdd(pairing.Id, candidate);
        if (!ReferenceEquals(winner, candidate))
        {
            // Lost a race with a concurrent connect for the same pairing — dispose the session
            // we optimistically started; it just stops its own supervisor loop.
            _ = candidate.DisposeAsync();
        }
        return winner;
    }

    public async Task DropAsync(Guid pairingId)
    {
        if (_sessions.TryRemove(pairingId, out var session))
            await session.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
    }
}
