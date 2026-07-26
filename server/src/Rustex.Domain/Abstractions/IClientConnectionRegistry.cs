namespace Rustex.Domain.Abstractions;

/// <summary>Which kind of client a live connection is, so alert delivery can pick an
/// appropriately intrusive channel. "App" means the frontend is running installed/standalone
/// (PWA) — see docs/ARCHITECTURE.md for why that's the closest thing to a native app this
/// stack can detect without a real native shell.</summary>
public enum ClientKind { Desktop, App }

/// <summary>Tracks which ClientKind(s) each authenticated user currently has connected, so
/// EmergencyAlertDispatcher can decide between a full-screen ring alert and a plain desktop
/// notification. Backed by an in-memory registry (Infrastructure) keyed off SignalR connection
/// lifecycle — state is per-process and intentionally not persisted; a reconnect just
/// re-registers.</summary>
public interface IClientConnectionRegistry
{
    void Register(string connectionId, Guid userId, ClientKind kind);
    void Unregister(string connectionId);
    IReadOnlySet<ClientKind> GetActiveKinds(Guid userId);
}
