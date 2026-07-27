using Microsoft.EntityFrameworkCore;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.Security;

namespace Rustex.Infrastructure.RustPlus.Fcm;

/// <summary>
/// Turns an FCM "Pair With Server" notification into a saved RustServer + RustPlusPairing.
/// Pulled out of RustPlusFcmListenerWorker as its own class specifically so it's unit-testable
/// against an in-memory database, without needing a live FCM connection.
/// </summary>
public sealed class RustServerPairingHandler
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly RustPlusConnectionManager _connectionManager;

    public RustServerPairingHandler(AppDbContext db, IEncryptionService encryption, RustPlusConnectionManager connectionManager)
    {
        _db = db;
        _encryption = encryption;
        _connectionManager = connectionManager;
    }

    public async Task<RustPlusPairing> HandleAsync(Guid userId, Notification<ServerEvent> notification, CancellationToken ct)
    {
        var ev = notification.Data;
        var server = await _db.RustServers.FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.IpAddress == ev.Ip, ct);

        if (server is null)
        {
            server = new RustServer
            {
                OwnerUserId = userId,
                Name = string.IsNullOrWhiteSpace(ev.Name) ? "Rust+ Server" : ev.Name,
                IpAddress = ev.Ip,
                // ev.Port is the Rust+ COMPANION port, not the game port — there's no reliable
                // way to derive the real game port from a pairing push. Flag it for the user to
                // verify rather than silently querying the wrong port.
                GamePort = ev.Port,
                FacepunchServerId = ev.Id,
                Description = ev.Desc,
                Tags = ["needs-review"],
            };
            _db.RustServers.Add(server);
        }
        else
        {
            server.FacepunchServerId ??= ev.Id;
            server.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct); // ensure server.Id exists before the pairing FK below

        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.UserId == userId && p.ServerId == server.Id, ct);
        if (pairing is null)
        {
            pairing = new RustPlusPairing { UserId = userId, ServerId = server.Id };
            _db.RustPlusPairings.Add(pairing);
        }
        else
        {
            await _connectionManager.DropAsync(pairing.Id);
        }

        pairing.PlayerId = notification.PlayerId;
        pairing.PlayerTokenEncrypted = _encryption.Encrypt(notification.PlayerToken.ToString());
        pairing.ServerIp = ev.Ip;
        pairing.ServerPort = ev.Port;

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Type = "rustplus.server_paired",
            Title = $"Paired with {server.Name}",
            Body = "Team tracking, vending search, and the rest are live for this server now.",
            RelatedEntityType = nameof(RustServer),
            RelatedEntityId = server.Id,
        });

        await _db.SaveChangesAsync(ct);

        _connectionManager.EnsureSession(pairing, notification.PlayerToken);

        return pairing;
    }
}
