namespace Rustex.Infrastructure.ServerQuery;

/// <summary>
/// Queries a Rust server's live status over its query port using the Source engine A2S_INFO
/// protocol — the same UDP protocol the Steam server browser uses. This works against any
/// public Rust server without a plugin or Rust+ pairing, but only exposes what the protocol
/// carries: population, map, and server name — not per-explosion telemetry (see
/// RustPlusEventSource / docs/ARCHITECTURE.md for that constraint).
/// </summary>
public interface IServerQueryClient
{
    Task<A2sInfoResult?> QueryAsync(string host, int queryPort, CancellationToken cancellationToken);
}

public sealed record A2sInfoResult(
    string ServerName,
    string MapName,
    int Players,
    int MaxPlayers,
    int Bots,
    string GameVersion,
    long RoundTripMs);
