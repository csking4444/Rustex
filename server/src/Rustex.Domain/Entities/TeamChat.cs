namespace Rustex.Domain.Entities;

/// <summary>In-app team chat message (distinct from automated in-game Rust team chat messages, see MessageTemplate).</summary>
public class TeamMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = default!;
    public Guid? UserId { get; set; }
    public string Content { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A per-event, per-server (optional) template used to auto-post into in-game Rust team chat.</summary>
public class MessageTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = default!;
    public Guid? ServerId { get; set; }
    public string EventType { get; set; } = default!; // RaidDetected, RocketFired, CargoShip, ...
    public string TemplateText { get; set; } = default!; // supports {server} {grid} {time} {event} {player} {count} {team} {weapon}
    public bool IsEnabled { get; set; } = true;
    public int CooldownSeconds { get; set; } = 30;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
