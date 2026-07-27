namespace Rustex.Domain.Entities;

/// <summary>A one-time, single-use setup code a signed-in user generates in the web UI and types
/// into the <c>rustex-pair</c> local helper. Redeeming it (RustPlusAccountController) exchanges
/// it for a narrowly-scoped JWT that can only call PUT credentials — never anything that reads
/// user data or mints a full session.</summary>
public class RustPlusLinkCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    /// <summary>SHA-256 of the code the user actually sees — the plaintext code is never stored.</summary>
    public string CodeHash { get; set; } = default!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? CreatedFromIp { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum RustPlusCredentialStatus
{
    /// <summary>Listening normally.</summary>
    Active,

    /// <summary>The FCM listener gave up after repeated failures, or the ~14-day Steam token
    /// window has likely elapsed — re-running `rustex-pair` fixes this. Already-paired servers
    /// keep working regardless; this only affects *new* pairing/alarm pushes.</summary>
    NeedsReauth,

    /// <summary>User explicitly turned off auto-pairing without deleting the credential.</summary>
    Disabled,
}

/// <summary>One user's Rust+ push credentials (GCM identity + FCM/Expo tokens), obtained by the
/// `rustex-pair` local helper and uploaded here so the server-side FCM listener
/// (RustPlusFcmListenerWorker) can keep listening for pairing/alarm pushes on their behalf.
/// Never sent back to any client once stored.</summary>
public class RustPlusAccountCredential
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public string? SteamId { get; set; }

    /// <summary>AES-GCM encrypted JSON matching RustPlusApi.Fcm.Data.Credentials
    /// ({ Gcm: { AndroidId, SecurityToken }, Fcm: { Token }, ExpoPushToken }).</summary>
    public string CredentialsEncrypted { get; set; } = default!;

    /// <summary>FCM message ids already delivered, so a listener restart doesn't replay old
    /// pairing/alarm pushes. Capped to the newest ~500 by the worker that maintains it.</summary>
    public string? PersistentIdsJson { get; set; }

    public RustPlusCredentialStatus Status { get; set; } = RustPlusCredentialStatus.Active;

    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastNotificationAt { get; set; }
    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
