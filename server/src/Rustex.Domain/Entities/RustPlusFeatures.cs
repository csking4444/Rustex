namespace Rustex.Domain.Entities;

// ---------- Team Tracking ----------

/// <summary>Last-known state per team member per server, driven by the teamChanged broadcast
/// (with a periodic getTeamInfo poll as a fallback) — persisted so online/offline/death
/// transitions survive a restart and don't spuriously re-fire notifications.</summary>
public class RustPlusTeamMemberState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;
    public ulong SteamId { get; set; }
    public string Name { get; set; } = default!;
    public bool IsOnline { get; set; }
    public bool IsAlive { get; set; } = true;
    public float LastX { get; set; }
    public float LastY { get; set; }
    public string? LastGrid { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------- Vending Search ----------

/// <summary>Populated by the vending poll worker so search reads the database, not the game
/// server, on every keystroke.</summary>
public class VendingMachineSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;
    public int MarkerId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public string? Grid { get; set; }
    public string? Name { get; set; }
    public bool OutOfStock { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<VendingListing> Listings { get; set; } = new List<VendingListing>();
}

public class VendingListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SnapshotId { get; set; }
    public VendingMachineSnapshot Snapshot { get; set; } = default!;
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public int CurrencyId { get; set; }
    public int CostPerItem { get; set; }
    public int AmountInStock { get; set; }
    public bool ItemIsBlueprint { get; set; }
    public bool CurrencyIsBlueprint { get; set; }
    public float? ItemCondition { get; set; }
    public float? ItemConditionMax { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------- Shop Alerts ----------

/// <summary>A user's watch on vending listings matching some criteria — RustPlusVendingPollWorker
/// diffs snapshots (via Rustex.Domain.RustPlus.VendingDiff) and matches changes against these.</summary>
public class ShopAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;

    public int? ItemId { get; set; }
    public string? ItemNameContains { get; set; }
    public int? MaxCostPerItem { get; set; }
    public int MinAmountInStock { get; set; } = 1;

    public bool NotifyOnNewListing { get; set; } = true;
    public bool NotifyOnPriceDrop { get; set; } = true;
    public bool NotifyOnRestock { get; set; } = true;

    public bool IsEnabled { get; set; } = true;
    public int CooldownSeconds { get; set; } = 900;
    public DateTimeOffset? LastTriggeredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------- Smart Devices ----------

public enum SmartDeviceKind { Switch, Alarm, StorageMonitor }

/// <summary>A paired Smart Switch/Alarm/Storage Monitor — populated by FCM entity-pairing pushes
/// (RustPlusFcmEventBus) or manual entry. A Smart Alarm's entityChanged.value==true is the only
/// genuine raid signal Rust+ can supply, so that specific transition also creates a RaidEvent.</summary>
public class RustPlusSmartDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;

    public long EntityId { get; set; }
    public SmartDeviceKind Type { get; set; }
    public string Name { get; set; } = default!;

    public bool? LastKnownValue { get; set; }
    public int? LastKnownCapacity { get; set; }
    public string? LastKnownItemsJson { get; set; }
    public bool AlarmRaisesRaidEvent { get; set; } = true;

    public DateTimeOffset? LastChangedAt { get; set; }
    public DateTimeOffset PairedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------- Chat Assistant ----------

/// <summary>Team chat relayed through Rust+, distinct from the existing in-app TeamMessage
/// (Rustex account team chat) — this is the live in-game feed.</summary>
public class RustPlusChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;
    public ulong SteamId { get; set; }
    public string Name { get; set; } = default!;
    public string Message { get; set; } = default!;
    public bool IsFromAssistant { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
