namespace Rustex.Domain.Entities;

public class RaidEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Grid { get; set; }
    public double? MapX { get; set; }
    public double? MapY { get; set; }
    public RaidTier Tier { get; set; } = RaidTier.Tier1;
    public string? RaidType { get; set; }
    public int ExplosionCount { get; set; }
    public string? EstimatedSize { get; set; }
    public RaidStatus Status { get; set; } = RaidStatus.Active;
    public EventSourceKind Source { get; set; } = EventSourceKind.Simulated;
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>Per-server raid-alarm configuration consumed by RaidAlarmEvaluator. One row per
/// server, created lazily with these defaults the first time it's requested or edited.</summary>
public class RaidAlarmSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Explosion count at/above which a cluster becomes Tier 1. Default: 1.</summary>
    public int Tier1Threshold { get; set; } = 1;
    /// <summary>Explosion count at/above which a cluster becomes Tier 2. Default: 3.</summary>
    public int Tier2Threshold { get; set; } = 3;
    /// <summary>Explosion count at/above which a cluster becomes Tier 3. Default: 5.</summary>
    public int Tier3Threshold { get; set; } = 5;

    /// <summary>Events land in the same cluster if no more than this many seconds pass
    /// between them. Default: 30.</summary>
    public int TimeWindowSeconds { get; set; } = 30;

    /// <summary>Events land in the same cluster only if within this many in-game units of the
    /// cluster's first event. Default: 50. Not meaningful until a source that supplies real
    /// map coordinates (a plugin bridge) is in use — see docs/ARCHITECTURE.md.</summary>
    public double ClusterRadius { get; set; } = 50;

    /// <summary>Minimum seconds between two alerts for the same server, regardless of how many
    /// clusters qualify in between. Default: 120.</summary>
    public int CooldownSeconds { get; set; } = 120;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
