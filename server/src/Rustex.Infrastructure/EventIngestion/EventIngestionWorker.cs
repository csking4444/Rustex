using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rustex.Domain;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Domain.RaidAlarm;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Infrastructure.EventIngestion;

/// <summary>
/// Consumes SimulatedEventSource for every registered server, clusters candidate events using
/// RaidAlarmEvaluator + each server's RaidAlarmSettings (falling back to defaults — 1/3/5
/// explosion thresholds for Tier 1/2/3, a 30s time window, a 120s cooldown), and persists a
/// RaidEvent for clusters that reach at least Tier 1. This is the real raid-alarm evaluation
/// pipeline; only the event *source* (SimulatedEventSource) is a placeholder — see
/// docs/ARCHITECTURE.md for why explosion-level telemetry can't come from Rust+ alone yet.
/// </summary>
public class EventIngestionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventIngestionWorker> _logger;
    private readonly Dictionary<Guid, Task> _perServerTasks = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastAlertAt = new();

    public EventIngestionWorker(IServiceScopeFactory scopeFactory, ILogger<EventIngestionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncServerTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EventIngestionWorker sync loop failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    private async Task SyncServerTasksAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var serverIds = await db.RustServers.Select(s => s.Id).ToListAsync(stoppingToken);

        foreach (var serverId in serverIds)
        {
            if (_perServerTasks.ContainsKey(serverId)) continue;
            _perServerTasks[serverId] = Task.Run(() => ConsumeServerAsync(serverId, stoppingToken), stoppingToken);
        }
    }

    private async Task ConsumeServerAsync(Guid serverId, CancellationToken stoppingToken)
    {
        var source = new SimulatedEventSource();
        var cluster = new List<RaidCandidateEvent>();
        var clusterLock = new object();

        await foreach (var candidate in source.StreamEventsAsync(serverId, stoppingToken))
        {
            var settings = await GetSettingsAsync(serverId, stoppingToken);
            if (!settings.IsEnabled) continue;

            List<RaidCandidateEvent>? toFlushImmediately = null;
            int scheduledCount;

            lock (clusterLock)
            {
                if (cluster.Count > 0 && !RaidAlarmEvaluator.BelongsToCluster(candidate, cluster, settings))
                {
                    toFlushImmediately = new List<RaidCandidateEvent>(cluster);
                    cluster.Clear();
                }

                cluster.Add(candidate);
                scheduledCount = cluster.Count;
            }

            if (toFlushImmediately is not null)
                await EvaluateAndPersistAsync(serverId, toFlushImmediately, stoppingToken);

            // Debounce: flush the cluster once TimeWindowSeconds pass without a new event
            // being added to it. `scheduledCount` guards against flushing a cluster that grew
            // in the meantime — a later-scheduled check will handle that flush instead.
            _ = Task.Delay(TimeSpan.FromSeconds(settings.TimeWindowSeconds), stoppingToken).ContinueWith(async _ =>
            {
                if (stoppingToken.IsCancellationRequested) return;

                List<RaidCandidateEvent>? flushed = null;
                lock (clusterLock)
                {
                    if (cluster.Count > 0 && cluster.Count == scheduledCount)
                    {
                        flushed = new List<RaidCandidateEvent>(cluster);
                        cluster.Clear();
                    }
                }

                if (flushed is not null)
                    await EvaluateAndPersistAsync(serverId, flushed, stoppingToken);
            }, stoppingToken);
        }
    }

    private async Task<RaidAlarmSettings> GetSettingsAsync(Guid serverId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await db.RaidAlarmSettings.FirstOrDefaultAsync(s => s.ServerId == serverId, ct);

        // Not persisted yet — use in-memory defaults without writing, so the hot ingestion path
        // never blocks on a write. RaidAlarmSettingsController creates the real row on first edit.
        return settings ?? new RaidAlarmSettings { ServerId = serverId };
    }

    private async Task EvaluateAndPersistAsync(Guid serverId, List<RaidCandidateEvent> cluster, CancellationToken ct)
    {
        try
        {
            var settings = await GetSettingsAsync(serverId, ct);
            if (!settings.IsEnabled) return;

            var tier = RaidAlarmEvaluator.ClassifyTier(cluster.Count, settings);
            if (tier is null) return; // didn't even reach Tier 1 under this server's thresholds

            var now = DateTimeOffset.UtcNow;
            if (_lastAlertAt.TryGetValue(serverId, out var lastAlert) &&
                now - lastAlert < TimeSpan.FromSeconds(settings.CooldownSeconds))
            {
                _logger.LogDebug("Suppressing raid alert for server {ServerId}: within {Cooldown}s cooldown", serverId, settings.CooldownSeconds);
                return;
            }

            var result = RaidAlarmEvaluator.BuildResult(cluster, tier.Value);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var broadcaster = scope.ServiceProvider.GetService<IRaidEventBroadcaster>();

            var raidEvent = new RaidEvent
            {
                ServerId = serverId,
                DetectedAt = result.DetectedAt,
                Grid = result.Grid,
                MapX = result.MapX,
                MapY = result.MapY,
                Tier = result.Tier,
                RaidType = result.RaidType,
                ExplosionCount = result.ExplosionCount,
                EstimatedSize = result.EstimatedSize,
                Status = RaidStatus.Active,
                Source = EventSourceKind.Simulated,
            };

            db.RaidEvents.Add(raidEvent);
            await db.SaveChangesAsync(ct);

            _lastAlertAt[serverId] = now;

            if (broadcaster is not null)
            {
                await broadcaster.BroadcastRaidEventCreatedAsync(serverId, new
                {
                    raidEvent.Id,
                    raidEvent.ServerId,
                    raidEvent.DetectedAt,
                    raidEvent.Grid,
                    raidEvent.Tier,
                    raidEvent.RaidType,
                    raidEvent.ExplosionCount,
                    raidEvent.EstimatedSize,
                }, ct);
            }

            var dispatcher = scope.ServiceProvider.GetService<IEmergencyAlertDispatcher>();
            if (dispatcher is not null)
                await dispatcher.DispatchAsync(raidEvent, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate/persist raid cluster for server {ServerId}", serverId);
        }
    }
}
