namespace Rustex.Domain.Entities;

/// <summary>A verified phone number a user can be called on. The number itself is encrypted at rest
/// (see Rustex.Infrastructure.Security.IEncryptionService) — this entity only ever holds ciphertext.</summary>
public class PhoneNumber
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string E164NumberEncrypted { get; set; } = default!;
    public string? Label { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsVerified { get; set; }
    public bool IsCallEnabled { get; set; } = true;
    public string? VerificationCodeHash { get; set; }
    public DateTimeOffset? VerificationExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Per-user (optionally per-server) rule for which raid triggers should place an emergency call.</summary>
public class CallAlertSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ServerId { get; set; } // null = applies to all servers
    public string TriggerType { get; set; } = default!; // RaidDetected | LargeRaid | SustainedExplosions | ...
    public bool IsEnabled { get; set; } = true;
    public RaidTier MinTier { get; set; } = RaidTier.Tier2;
    public int MinExplosionCount { get; set; } = 3;
    public int CooldownSeconds { get; set; } = 300;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A single outbound emergency call attempt and its outcome, forming the escalation/audit trail.</summary>
public class CallAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ServerId { get; set; }
    public Guid? RaidEventId { get; set; }
    public Guid PhoneNumberId { get; set; }
    public CallProvider Provider { get; set; }
    public CallStatus Status { get; set; } = CallStatus.Queued;
    public int? DurationSeconds { get; set; }
    public int RetryCount { get; set; }
    public bool Acknowledged { get; set; }
    public DateTimeOffset InitiatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AnsweredAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Error { get; set; }
}
