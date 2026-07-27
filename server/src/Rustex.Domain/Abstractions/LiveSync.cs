using System.Text.Json;

namespace Rustex.Domain.Abstractions;

/// <summary>Addresses one stream of live state. A struct with factory methods rather than a bare
/// string so a typo'd scope name is a compile error instead of a silently-empty subscription.</summary>
public readonly record struct LiveScope(string Kind, Guid Id)
{
    public const string ServerKind = "server";
    public const string UserKind = "user";

    public static LiveScope Server(Guid serverId) => new(ServerKind, serverId);
    public static LiveScope User(Guid userId) => new(UserKind, userId);

    public override string ToString() => $"{Kind}:{Id}";

    /// <summary>Parses the wire form a client sends when subscribing. Returns false for anything
    /// malformed — callers must not fall back to a default scope, since that would hand the
    /// caller someone else's stream.</summary>
    public static bool TryParse(string? raw, out LiveScope scope)
    {
        scope = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parts = raw.Split(':', 2);
        if (parts.Length != 2) return false;
        if (parts[0] is not (ServerKind or UserKind)) return false;
        if (!Guid.TryParse(parts[1], out var id)) return false;

        scope = new LiveScope(parts[0], id);
        return true;
    }
}

/// <summary>Section names within a snapshot. Each producer owns one section and updates it
/// independently, so the team-tracking worker writing a roster can never clobber the status
/// poller's ping figure.</summary>
public static class LiveSections
{
    public const string Status = "status";
    public const string Team = "team";
    public const string Devices = "devices";
    public const string RaidActivity = "raid_activity";
    public const string Vending = "vending";
}

/// <summary>Everything currently known about a scope, plus the version it was captured at.
/// A client that reconnects fetches this and is immediately current, rather than waiting for the
/// next push — which for a 30s poll could otherwise leave a stale dashboard on screen.</summary>
public sealed record LiveSnapshot(
    string Scope,
    long Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, JsonElement> Sections);

/// <summary>A single section update as pushed to clients. <see cref="Version"/> is monotonic per
/// scope, so a client that sees a gap knows it missed a message and can re-fetch the snapshot
/// instead of rendering state it cannot trust.</summary>
public sealed record LiveUpdate(string Scope, string Section, long Version, DateTimeOffset At, object Payload);

/// <summary>Durable-ish cache of current live state, keyed by scope and section.</summary>
public interface ILiveStateStore
{
    /// <summary>Writes one section and returns the scope's new version number.</summary>
    Task<long> SetSectionAsync(LiveScope scope, string section, object payload, CancellationToken ct);

    Task<LiveSnapshot?> GetSnapshotAsync(LiveScope scope, CancellationToken ct);

    Task ClearAsync(LiveScope scope, CancellationToken ct);
}

/// <summary>Fans a live update out to whoever is currently connected and listening to a scope.
/// Implemented over SignalR in the Api project; kept here so Infrastructure workers don't need a
/// reference to the web layer (same reason as <see cref="IRaidEventBroadcaster"/>).</summary>
public interface ILiveBroadcaster
{
    Task BroadcastAsync(LiveScope scope, LiveUpdate update, CancellationToken ct);
}

/// <summary>The thing background workers actually call: caches the new state and pushes it, with
/// retry if the push fails.</summary>
public interface ILiveSyncPublisher
{
    Task PublishAsync(LiveScope scope, string section, object payload, CancellationToken ct);
}
