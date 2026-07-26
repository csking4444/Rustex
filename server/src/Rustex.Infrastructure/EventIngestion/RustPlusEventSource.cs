using Rustex.Domain.Abstractions;

namespace Rustex.Infrastructure.EventIngestion;

/// <summary>
/// Stub for the official Rust+ companion protocol (FCM device pairing + a protobuf/WebSocket
/// app connection to the paired server). Documented here so the constraint is explicit rather
/// than silently missing:
///
///   Rust+ CAN supply: server population/queue, some monument state pushed to the app,
///   team member info for the pairing account.
///   Rust+ CANNOT supply: explosion events, weapon types, combat log, or anything needed for
///   real per-explosion raid detection. That requires a server-side plugin bridge (Phase 3).
///
/// This class exists so the ingestion pipeline has a real (if limited) live implementation to
/// register, and so `SupportsExplosionDetail = false` is enforced in code, not just docs —
/// callers that need explosion detail must check this flag and fail closed rather than emit
/// fabricated data.
/// </summary>
public class RustPlusEventSource : IEventSource
{
    public string Name => "rustplus";
    public bool SupportsExplosionDetail => false;

    public IAsyncEnumerable<RaidCandidateEvent> StreamEventsAsync(Guid serverId, CancellationToken cancellationToken)
    {
        // Pairing handshake (FCM listener -> Rust+ app-pairing payload -> WebSocket to the
        // paired server) is a Phase 3+ integration once a real Discord-linked Rust+ pairing
        // flow is implemented. Left unimplemented deliberately rather than faked.
        throw new NotSupportedException(
            "RustPlusEventSource is not yet wired to a live pairing session. " +
            "It intentionally does not support explosion-level events — see the class doc comment.");
    }
}
