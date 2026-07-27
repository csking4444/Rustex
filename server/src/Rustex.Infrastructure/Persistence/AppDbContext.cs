using Microsoft.EntityFrameworkCore;
using Rustex.Domain.Entities;

namespace Rustex.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<TeamRole> TeamRoles => Set<TeamRole>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<TeamInvite> TeamInvites => Set<TeamInvite>();

    public DbSet<RustServer> RustServers => Set<RustServer>();
    public DbSet<ServerStatusSnapshot> ServerStatusSnapshots => Set<ServerStatusSnapshot>();
    public DbSet<RustPlusPairing> RustPlusPairings => Set<RustPlusPairing>();
    public DbSet<RustPlusLinkCode> RustPlusLinkCodes => Set<RustPlusLinkCode>();
    public DbSet<RustPlusAccountCredential> RustPlusAccountCredentials => Set<RustPlusAccountCredential>();

    public DbSet<RaidEvent> RaidEvents => Set<RaidEvent>();
    public DbSet<RaidAlarmSettings> RaidAlarmSettings => Set<RaidAlarmSettings>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationHistory> NotificationHistory => Set<NotificationHistory>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    public DbSet<MapData> Maps => Set<MapData>();
    public DbSet<Marker> Markers => Set<Marker>();

    public DbSet<TeamMessage> TeamMessages => Set<TeamMessage>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AnalyticsSnapshot> AnalyticsSnapshots => Set<AnalyticsSnapshot>();

    public DbSet<PhoneNumber> PhoneNumbers => Set<PhoneNumber>();
    public DbSet<CallAlertSetting> CallAlertSettings => Set<CallAlertSetting>();
    public DbSet<CallAlert> CallAlerts => Set<CallAlert>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---------- Identity ----------
        b.Entity<User>(e =>
        {
            // Postgres unique indexes treat NULL as distinct from every other value, so these
            // stay correct with DiscordId/SteamId/Email all nullable — any number of users can
            // have no Discord/Steam link, but two users can't share the same linked one.
            e.HasIndex(x => x.DiscordId).IsUnique();
            e.HasIndex(x => x.SteamId).IsUnique();
            e.HasIndex(x => x.GoogleId).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Profile).WithOne(p => p.User).HasForeignKey<UserProfile>(p => p.UserId);
            e.HasOne(x => x.Settings).WithOne(s => s.User).HasForeignKey<UserSettings>(s => s.UserId);
        });
        b.Entity<UserProfile>().HasKey(x => x.UserId);
        b.Entity<UserSettings>().HasKey(x => x.UserId);

        b.Entity<Session>(e =>
        {
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany(u => u.Sessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Teams ----------
        b.Entity<Team>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Permission>().HasIndex(x => x.Key).IsUnique();

        b.Entity<TeamRole>(e =>
        {
            e.HasIndex(x => new { x.TeamId, x.Name }).IsUnique();
            e.HasOne(x => x.Team).WithMany(t => t.Roles).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Permissions).WithMany()
                .UsingEntity(j => j.ToTable("team_role_permissions"));
        });

        b.Entity<TeamMember>(e =>
        {
            e.HasIndex(x => new { x.TeamId, x.UserId }).IsUnique();
            e.HasOne(x => x.Team).WithMany(t => t.Members).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany(u => u.TeamMemberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<TeamInvite>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>();
        });

        // ---------- Servers ----------
        b.Entity<RustServer>(e =>
        {
            e.HasOne(x => x.Team).WithMany(t => t.Servers).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Tags).HasColumnType("text[]");
        });

        b.Entity<ServerStatusSnapshot>(e =>
        {
            e.HasOne(x => x.Server).WithMany(s => s.StatusSnapshots).HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ServerId, x.RecordedAt });
        });

        b.Entity<RustPlusPairing>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Server).WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.ServerId }).IsUnique();
        });

        b.Entity<RustPlusLinkCode>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CodeHash).IsUnique();
        });

        b.Entity<RustPlusAccountCredential>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne(x => x.User).WithOne().HasForeignKey<RustPlusAccountCredential>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>();
        });

        // ---------- Raid events ----------
        b.Entity<RaidEvent>(e =>
        {
            e.HasOne(x => x.Server).WithMany(s => s.RaidEvents).HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ServerId, x.DetectedAt });
            e.Property(x => x.Tier).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Source).HasConversion<string>();
            e.Property(x => x.MetadataJson).HasColumnType("jsonb");
        });

        b.Entity<RaidAlarmSettings>(e =>
        {
            e.HasOne(x => x.Server).WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ServerId).IsUnique();
        });

        // ---------- Notifications ----------
        b.Entity<Notification>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.Severity).HasConversion<string>();
        });

        b.Entity<NotificationHistory>(e =>
        {
            e.HasOne(x => x.Notification).WithMany(n => n.History).HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Channel).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<Webhook>(e =>
        {
            e.Property(x => x.EventTypes).HasColumnType("text[]");
        });

        b.Entity<PushSubscription>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Endpoint).IsUnique();
        });

        // ---------- Maps ----------
        b.Entity<MapData>(e =>
        {
            e.HasOne(x => x.Server).WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.MonumentDataJson).HasColumnType("jsonb");
        });

        b.Entity<Marker>(e =>
        {
            e.HasOne(x => x.Map).WithMany(m => m.Markers).HasForeignKey(x => x.MapId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Type).HasConversion<string>();
        });

        // ---------- Team chat ----------
        b.Entity<TeamMessage>(e =>
        {
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MessageTemplate>(e =>
        {
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TeamId, x.ServerId, x.EventType }).IsUnique();
        });

        // ---------- Logging / analytics ----------
        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.TeamId, x.CreatedAt });
            e.Property(x => x.MetadataJson).HasColumnType("jsonb");
        });

        b.Entity<AnalyticsSnapshot>(e =>
        {
            e.HasOne(x => x.Server).WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.PlayerActivityJson).HasColumnType("jsonb");
        });

        // ---------- Phone / emergency calls ----------
        b.Entity<PhoneNumber>(e =>
        {
            e.HasOne(x => x.User).WithMany(u => u.PhoneNumbers).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CallAlertSetting>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.ServerId, x.TriggerType }).IsUnique();
            e.Property(x => x.MinTier).HasConversion<string>();
        });

        b.Entity<CallAlert>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.InitiatedAt });
            e.Property(x => x.Provider).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
        });
    }
}
