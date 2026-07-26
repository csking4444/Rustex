namespace Rustex.Domain.Entities;

public class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = default!;
    public string? IconUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
    public ICollection<TeamRole> Roles { get; set; } = new List<TeamRole>();
    public ICollection<RustServer> Servers { get; set; } = new List<RustServer>();
}

public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = default!; // e.g. "servers.manage"
    public string? Description { get; set; }
}

public class TeamRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = default!;
    public string Name { get; set; } = default!; // Owner / Admin / Member / custom
    public bool IsSystem { get; set; }

    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}

public class TeamMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = default!;
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public Guid RoleId { get; set; }
    public TeamRole Role { get; set; } = default!;
    public TeamMemberStatus Status { get; set; } = TeamMemberStatus.Active;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TeamInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = default!;
    public Guid InviterId { get; set; }
    public string? InviteeDiscord { get; set; }
    public string Token { get; set; } = default!;
    public InviteStatus Status { get; set; } = InviteStatus.Pending;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
