using Rustex.Domain.Abstractions;

namespace Rustex.Infrastructure.EventIngestion;

/// <summary>
/// Generates synthetic candidate events on a timer so the raid-alarm pipeline, dashboard,
/// and notification fan-out can be built and demoed without a live Rust server or plugin.
/// Never used in production for a server flagged as "real" — see RustPlusEventSource for
/// what an actual (limited) live source looks like.
/// </summary>
public class SimulatedEventSource : IEventSource
{
    public string Name => "simulated";
    public bool SupportsExplosionDetail => true;

    private static readonly string[] ExplosionTypes = { "rocket", "hv_rocket", "satchel", "c4", "explosive_ammo" };
    private static readonly string[] Grids = { "A7", "B12", "C4", "D9", "F15", "G3", "H21" };
    private readonly Random _random = new();

    public async IAsyncEnumerable<RaidCandidateEvent> StreamEventsAsync(
        Guid serverId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delaySeconds = _random.Next(20, 90);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

            var grid = Grids[_random.Next(Grids.Length)];
            var burstSize = _random.Next(1, 6);

            for (var i = 0; i < burstSize; i++)
            {
                yield return new RaidCandidateEvent(
                    ServerId: serverId,
                    OccurredAt: DateTimeOffset.UtcNow,
                    EventType: ExplosionTypes[_random.Next(ExplosionTypes.Length)],
                    Grid: grid,
                    MapX: _random.NextDouble() * 4000,
                    MapY: _random.NextDouble() * 4000,
                    PlayerName: null,
                    Metadata: new Dictionary<string, string> { ["simulated"] = "true" });

                if (i < burstSize - 1)
                    await Task.Delay(TimeSpan.FromSeconds(_random.Next(1, 5)), cancellationToken);
            }
        }
    }
}
