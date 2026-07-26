namespace Rustex.Domain.Abstractions;

/// <summary>
/// A source of raw, per-server candidate events that the raid-alarm evaluation pipeline consumes.
/// Implementations live in Rustex.Infrastructure.EventIngestion. See docs/ARCHITECTURE.md
/// for why this is an abstraction: Rust+ can only supply a subset of what a companion plugin can.
/// </summary>
public interface IEventSource
{
    /// <summary>Machine-readable identifier, persisted on RaidEvent.Source (e.g. "simulated", "rustplus", "plugin").</summary>
    string Name { get; }

    /// <summary>True if this source can supply explosion-level detail (weapon type, position, count).
    /// Rust+ cannot; a plugin bridge can. Callers use this to decide whether real raid detection is possible.</summary>
    bool SupportsExplosionDetail { get; }

    IAsyncEnumerable<RaidCandidateEvent> StreamEventsAsync(Guid serverId, CancellationToken cancellationToken);
}

/// <summary>A single raw event observed on a server, before raid-alarm clustering/severity scoring is applied.</summary>
public sealed record RaidCandidateEvent(
    Guid ServerId,
    DateTimeOffset OccurredAt,
    string EventType,       // e.g. "rocket", "c4", "satchel", "mlrs", "player_connected", "cargo_ship"
    string? Grid,
    double? MapX,
    double? MapY,
    string? PlayerName,
    IReadOnlyDictionary<string, string>? Metadata);
