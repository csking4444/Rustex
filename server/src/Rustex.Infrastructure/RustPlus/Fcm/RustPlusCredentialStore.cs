using Microsoft.EntityFrameworkCore;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.Security;

namespace Rustex.Infrastructure.RustPlus.Fcm;

/// <summary>Encrypts and persists one user's Rust+ FCM credentials — the bridge between
/// RustPlusAccountController (which receives them from `rustex-pair`) and
/// RustPlusFcmListenerWorker (which needs them to keep listening). Confines the
/// RustPlusApi.Fcm.Registration dependency to this one file's imports rather than spreading
/// vendor types across the API layer.</summary>
public interface IRustPlusCredentialStore
{
    Task SaveAsync(Guid userId, Credentials credentials, string? steamId, CancellationToken ct);
    Task<Credentials?> LoadAsync(Guid userId, CancellationToken ct);
    Task<RustPlusAccountCredential?> GetAsync(Guid userId, CancellationToken ct);
    Task<List<RustPlusAccountCredential>> GetAllAsync(RustPlusCredentialStatus? status, CancellationToken ct);
    Task DeleteAsync(Guid userId, CancellationToken ct);
    Task SavePersistentIdsAsync(Guid userId, IReadOnlyCollection<string> persistentIds, CancellationToken ct);
    Task<List<string>> LoadPersistentIdsAsync(Guid userId, CancellationToken ct);
    Task MarkConnectedAsync(Guid userId, CancellationToken ct);
    Task MarkNotificationAsync(Guid userId, CancellationToken ct);
    Task RecordFailureAsync(Guid userId, int failuresBeforeReauth, CancellationToken ct);
}

public sealed class RustPlusCredentialStore : IRustPlusCredentialStore
{
    // The FCM listener mutates the persistentIds collection it's given; without a cap, a
    // long-lived connection's dedupe set would grow forever.
    private const int MaxPersistentIds = 500;

    private readonly AppDbContext _db;
    private readonly IEncryptionService _encryption;

    public RustPlusCredentialStore(AppDbContext db, IEncryptionService encryption)
    {
        _db = db;
        _encryption = encryption;
    }

    public async Task SaveAsync(Guid userId, Credentials credentials, string? steamId, CancellationToken ct)
    {
        var row = await _db.RustPlusAccountCredentials.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var encrypted = _encryption.Encrypt(CredentialsStore.Serialize(credentials));

        if (row is null)
        {
            row = new RustPlusAccountCredential { UserId = userId };
            _db.RustPlusAccountCredentials.Add(row);
        }

        row.CredentialsEncrypted = encrypted;
        row.SteamId = steamId ?? row.SteamId;
        row.Status = RustPlusCredentialStatus.Active;
        row.RegisteredAt = DateTimeOffset.UtcNow;
        row.ConsecutiveFailures = 0;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<Credentials?> LoadAsync(Guid userId, CancellationToken ct)
    {
        var row = await _db.RustPlusAccountCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return null;

        return CredentialsStore.Deserialize(_encryption.Decrypt(row.CredentialsEncrypted));
    }

    public Task<RustPlusAccountCredential?> GetAsync(Guid userId, CancellationToken ct) =>
        _db.RustPlusAccountCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);

    public Task<List<RustPlusAccountCredential>> GetAllAsync(RustPlusCredentialStatus? status, CancellationToken ct)
    {
        var query = _db.RustPlusAccountCredentials.AsNoTracking().AsQueryable();
        if (status is not null) query = query.Where(x => x.Status == status);
        return query.ToListAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken ct)
    {
        var row = await _db.RustPlusAccountCredentials.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return;
        _db.RustPlusAccountCredentials.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SavePersistentIdsAsync(Guid userId, IReadOnlyCollection<string> persistentIds, CancellationToken ct)
    {
        var row = await _db.RustPlusAccountCredentials.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return;

        var capped = persistentIds.Skip(Math.Max(0, persistentIds.Count - MaxPersistentIds)).ToList();
        row.PersistentIdsJson = System.Text.Json.JsonSerializer.Serialize(capped);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<string>> LoadPersistentIdsAsync(Guid userId, CancellationToken ct)
    {
        var row = await _db.RustPlusAccountCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row?.PersistentIdsJson is null) return [];
        return System.Text.Json.JsonSerializer.Deserialize<List<string>>(row.PersistentIdsJson) ?? [];
    }

    public async Task MarkConnectedAsync(Guid userId, CancellationToken ct)
    {
        var row = await _db.RustPlusAccountCredentials.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return;
        row.LastConnectedAt = DateTimeOffset.UtcNow;
        row.ConsecutiveFailures = 0;
        if (row.Status == RustPlusCredentialStatus.NeedsReauth) row.Status = RustPlusCredentialStatus.Active;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkNotificationAsync(Guid userId, CancellationToken ct)
    {
        var row = await _db.RustPlusAccountCredentials.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return;
        row.LastNotificationAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RecordFailureAsync(Guid userId, int failuresBeforeReauth, CancellationToken ct)
    {
        var row = await _db.RustPlusAccountCredentials.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return;
        row.ConsecutiveFailures++;
        if (row.ConsecutiveFailures >= failuresBeforeReauth) row.Status = RustPlusCredentialStatus.NeedsReauth;
        await _db.SaveChangesAsync(ct);
    }
}
