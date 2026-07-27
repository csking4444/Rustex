namespace Rustex.Domain.Entities;

public class RustServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }
    public Guid OwnerUserId { get; set; }
    public User Owner { get; set; } = default!;

    public string Name { get; set; } = default!;
    public string IpAddress { get; set; } = default!;
    public int GamePort { get; set; }
    public int? QueryPort { get; set; }
    public string? MapName { get; set; }
    public long? Seed { get; set; }
    public int? WorldSize { get; set; }
    public string? Description { get; set; }

    /// <summary>Facepunch's own server GUID, learned from an FCM "Pair With Server" push. Lets a
    /// later Smart Alarm/entity push (which only carries this GUID, not our RustServer.Id) be
    /// attributed back to the right server. Null for servers added via manual pairing only.</summary>
    public Guid? FacepunchServerId { get; set; }
    public ServerStatus Status { get; set; } = ServerStatus.Unknown;
    public List<string> Tags { get; set; } = new();
    public bool IsFavorite { get; set; }
    public string? WipeSchedule { get; set; }
    public string? RestartSchedule { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ServerStatusSnapshot> StatusSnapshots { get; set; } = new List<ServerStatusSnapshot>();
    public ICollection<RaidEvent> RaidEvents { get; set; } = new List<RaidEvent>();
}

/// <summary>Point-in-time ping/population sample, used for live status + analytics history.</summary>
public class ServerStatusSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;
    public int? PingMs { get; set; }
    public int? PlayerCount { get; set; }
    public int? MaxPlayers { get; set; }
    public int? QueueSize { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
