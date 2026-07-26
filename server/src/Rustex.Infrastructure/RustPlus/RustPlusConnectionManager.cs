using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rustex.Domain.Entities;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>Keeps at most one live RustPlusClient per pairing, connecting lazily on first use.
/// Registered as a singleton so connections outlive individual HTTP requests — a Rust+
/// WebSocket is meant to be a long-lived connection, not reconnected per API call.</summary>
public class RustPlusConnectionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, RustPlusClient> _clients = new();
    private readonly ILoggerFactory _loggerFactory;

    public RustPlusConnectionManager(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public async Task<RustPlusClient> GetOrConnectAsync(RustPlusPairing pairing, uint playerToken, CancellationToken ct)
    {
        if (_clients.TryGetValue(pairing.Id, out var existing)) return existing;

        var client = new RustPlusClient(pairing.PlayerId, playerToken, _loggerFactory.CreateLogger<RustPlusClient>());
        await client.ConnectAsync(pairing.ServerIp, pairing.ServerPort, ct);

        if (_clients.TryAdd(pairing.Id, client)) return client;

        // Lost a race with a concurrent connect for the same pairing — use the winner's client.
        await client.DisposeAsync();
        return _clients[pairing.Id];
    }

    public async Task DropAsync(Guid pairingId)
    {
        if (_clients.TryRemove(pairingId, out var client))
            await client.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
            await client.DisposeAsync();
    }
}
