namespace Rustex.Domain.Entities;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Body { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public bool IsRead { get; set; }
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<NotificationHistory> History { get; set; } = new List<NotificationHistory>();
}

public class NotificationHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public Notification Notification { get; set; } = default!;
    public NotificationChannel Channel { get; set; }
    public DeliveryStatus Status { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? Error { get; set; }
}

public class Webhook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TeamId { get; set; }
    public Guid? ServerId { get; set; }
    public string Url { get; set; } = default!;
    public string Secret { get; set; } = default!;
    public List<string> EventTypes { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
