namespace Rustex.Domain.Entities;

/// <summary>Credentials for one user's Rust+ pairing to one server. Obtained either manually
/// (the user enters a token they got some other way — e.g. a community pairing tool) or,
/// eventually, automatically via the FCM push-pairing flow. PlayerToken is encrypted at rest
/// via IEncryptionService, same as PhoneNumber — it's a live authentication secret.</summary>
public class RustPlusPairing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;

    public ulong PlayerId { get; set; }
    public string PlayerTokenEncrypted { get; set; } = default!;
    public string ServerIp { get; set; } = default!;
    public int ServerPort { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastConnectedAt { get; set; }
}
