using Rustex.Domain.Entities;

namespace Rustex.Domain.Abstractions;

/// <summary>Decides who gets notified about a qualifying RaidEvent and through which channel
/// (ring alert vs plain notification) — see EmergencyAlertDispatcher (Infrastructure) for the
/// implementation, kept behind an interface so EventIngestionWorker depends on the contract,
/// not the EF-backed implementation.</summary>
public interface IEmergencyAlertDispatcher
{
    Task DispatchAsync(RaidEvent raidEvent, CancellationToken ct);
}
