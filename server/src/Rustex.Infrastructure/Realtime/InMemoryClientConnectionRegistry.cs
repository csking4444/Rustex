using System.Collections.Concurrent;
using Rustex.Domain.Abstractions;

namespace Rustex.Infrastructure.Realtime;

public class InMemoryClientConnectionRegistry : IClientConnectionRegistry
{
    private sealed record Connection(Guid UserId, ClientKind Kind);

    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public void Register(string connectionId, Guid userId, ClientKind kind) =>
        _connections[connectionId] = new Connection(userId, kind);

    public void Unregister(string connectionId) => _connections.TryRemove(connectionId, out _);

    public IReadOnlySet<ClientKind> GetActiveKinds(Guid userId) =>
        _connections.Values
            .Where(c => c.UserId == userId)
            .Select(c => c.Kind)
            .ToHashSet();
}
