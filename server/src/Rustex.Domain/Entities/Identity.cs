namespace Rustex.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DiscordId { get; set; } = default!;
    public string DiscordUsername { get; set; } = default!;
    public string? DiscordAvatar { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public UserProfile? Profile { get; set; }
    public UserSettings? Settings { get; set; }
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public ICollection<TeamMember> TeamMemberships { get; set; } = new List<TeamMember>();
    public ICollection<PhoneNumber> PhoneNumbers { get; set; } = new List<PhoneNumber>();
}

public class UserProfile
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string Locale { get; set; } = "en-US";
    public string? AvatarUrl { get; set; }
}

public class UserSettings
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string Theme { get; set; } = "tactical-dark";
    public bool SoundEnabled { get; set; } = true;
    public bool DesktopEnabled { get; set; } = true;
    public bool BrowserEnabled { get; set; } = true;
    public bool DiscordEnabled { get; set; }
    public bool PushEnabled { get; set; }
    public bool CallEnabled { get; set; }
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
    public string QuietHoursTimezone { get; set; } = "UTC";
    public string MuteScheduleJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A rotating refresh-token session. The token itself is never stored — only its hash.</summary>
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string RefreshTokenHash { get; set; } = default!;
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
