using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;

namespace Rustex.Domain.RaidAlarm;

/// <summary>
/// Pure raid-alarm logic: given a buffer of candidate events and a server's settings, decides
/// whether consecutive events belong to the same cluster, and what tier a finished cluster
/// reaches. No framework dependencies — EventIngestionWorker (Infrastructure) owns the actual
/// streaming/buffering, DB access, and cooldown state; this class only makes the yes/no calls.
///
/// Tiering is deliberately a plain count threshold, not a subjective severity judgment: by
/// default 1+ explosions in a cluster is Tier 1, 3+ is Tier 2, 5+ is Tier 3. All three
/// thresholds are per-server settings (RaidAlarmSettings), not hardcoded — these are just the
/// defaults.
/// </summary>
public static class RaidAlarmEvaluator
{
    /// <summary>True if `candidate` should be folded into `cluster` rather than starting a new
    /// one — both close enough in time (TimeWindowSeconds since the cluster's last event) and,
    /// when coordinates are available, close enough in space (ClusterRadius from the cluster's
    /// first event) to plausibly be the same raid.</summary>
    public static bool BelongsToCluster(
        RaidCandidateEvent candidate,
        IReadOnlyList<RaidCandidateEvent> cluster,
        RaidAlarmSettings settings)
    {
        if (cluster.Count == 0) return true;

        var last = cluster[^1];
        if (candidate.OccurredAt - last.OccurredAt > TimeSpan.FromSeconds(settings.TimeWindowSeconds))
            return false;

        var first = cluster[0];
        if (first.MapX is null || first.MapY is null || candidate.MapX is null || candidate.MapY is null)
            return true; // no coordinates to compare — don't split on unknown data

        var dx = candidate.MapX.Value - first.MapX.Value;
        var dy = candidate.MapY.Value - first.MapY.Value;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        return distance <= settings.ClusterRadius;
    }

    /// <summary>Classifies a finished cluster's explosion count into a tier using the server's
    /// thresholds. Returns null if the cluster doesn't even reach Tier 1 (shouldn't normally
    /// happen since Tier1Threshold defaults to 1, but a server could configure it higher).</summary>
    public static RaidTier? ClassifyTier(int explosionCount, RaidAlarmSettings settings)
    {
        if (explosionCount >= settings.Tier3Threshold) return RaidTier.Tier3;
        if (explosionCount >= settings.Tier2Threshold) return RaidTier.Tier2;
        if (explosionCount >= settings.Tier1Threshold) return RaidTier.Tier1;
        return null;
    }

    /// <summary>Builds the descriptive fields for a finished, qualifying cluster.</summary>
    public static RaidAlarmResult BuildResult(IReadOnlyList<RaidCandidateEvent> cluster, RaidTier tier)
    {
        var first = cluster[0];
        var distinctTypes = cluster.Select(e => e.EventType).Distinct().Count();

        var estimatedSize = tier switch
        {
            RaidTier.Tier3 => "large_team",
            RaidTier.Tier2 => "medium_team",
            _ => "small_team",
        };

        return new RaidAlarmResult(
            Tier: tier,
            ExplosionCount: cluster.Count,
            Grid: first.Grid,
            MapX: first.MapX,
            MapY: first.MapY,
            RaidType: distinctTypes > 1 ? "mixed" : first.EventType,
            EstimatedSize: estimatedSize,
            DetectedAt: first.OccurredAt);
    }
}

public sealed record RaidAlarmResult(
    RaidTier Tier,
    int ExplosionCount,
    string? Grid,
    double? MapX,
    double? MapY,
    string RaidType,
    string EstimatedSize,
    DateTimeOffset DetectedAt);
