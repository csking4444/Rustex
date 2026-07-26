namespace Rustex.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public Guid? TeamId { get; set; }
    public string Action { get; set; } = default!;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AnalyticsSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public int RaidCount { get; set; }
    public int? PeakHourUtc { get; set; }
    public int? AvgPingMs { get; set; }
    public string PlayerActivityJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
