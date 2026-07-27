using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus;
using Rustex.Infrastructure.RustPlus.Fcm;
using Rustex.Infrastructure.Security;
using Xunit;

namespace Rustex.Api.Tests.RustPlus;

/// <summary>Exercises the actual pairing-notification handling logic against a real (in-memory)
/// AppDbContext — the one piece of RustPlusFcmListenerWorker that's genuinely testable without a
/// live FCM connection, which is why it was pulled out into its own class.</summary>
public class RustServerPairingHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly RustServerPairingHandler _handler;

    public RustServerPairingHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var encryption = new AesGcmEncryptionService(Convert.ToBase64String(new byte[32]));
        _connectionManager = new RustPlusConnectionManager(NullLoggerFactory.Instance);
        _handler = new RustServerPairingHandler(_db, encryption, _connectionManager);
    }

    private static Notification<ServerEvent> MakeNotification(string ip = "203.0.113.5", int port = 28083, string name = "Rustex Main", ulong playerId = 76561198000000001, int playerToken = -12345) =>
        new()
        {
            PlayerId = playerId,
            PlayerToken = playerToken,
            Data = new ServerEvent { Id = Guid.NewGuid(), Name = name, Ip = ip, Port = port, Desc = "test server" },
        };

    [Fact]
    public async Task NoExistingServer_CreatesOneWithFacepunchServerId()
    {
        var userId = Guid.NewGuid();
        var notification = MakeNotification();

        var pairing = await _handler.HandleAsync(userId, notification, CancellationToken.None);

        var server = await _db.RustServers.FirstAsync(s => s.Id == pairing.ServerId);
        Assert.Equal(userId, server.OwnerUserId);
        Assert.Equal("Rustex Main", server.Name);
        Assert.Equal(notification.Data.Id, server.FacepunchServerId);
        Assert.Contains("needs-review", server.Tags);
    }

    [Fact]
    public async Task ExistingServerWithSameIp_IsReusedNotDuplicated()
    {
        var userId = Guid.NewGuid();
        await _handler.HandleAsync(userId, MakeNotification(ip: "203.0.113.5"), CancellationToken.None);

        await _handler.HandleAsync(userId, MakeNotification(ip: "203.0.113.5"), CancellationToken.None);

        var servers = await _db.RustServers.Where(s => s.OwnerUserId == userId).ToListAsync();
        Assert.Single(servers);
    }

    [Fact]
    public async Task PairingIsUpserted_OnUniqueUserServerIndex()
    {
        var userId = Guid.NewGuid();
        var first = await _handler.HandleAsync(userId, MakeNotification(playerToken: -111), CancellationToken.None);

        var second = await _handler.HandleAsync(userId, MakeNotification(playerToken: -222), CancellationToken.None);

        Assert.Equal(first.Id, second.Id); // same pairing row, updated in place
        var pairings = await _db.RustPlusPairings.Where(p => p.UserId == userId).ToListAsync();
        Assert.Single(pairings);
    }

    [Fact]
    public async Task DuplicatePush_IsHandledIdempotently()
    {
        var userId = Guid.NewGuid();
        var notification = MakeNotification();

        await _handler.HandleAsync(userId, notification, CancellationToken.None);
        await _handler.HandleAsync(userId, notification, CancellationToken.None);
        await _handler.HandleAsync(userId, notification, CancellationToken.None);

        Assert.Single(await _db.RustServers.Where(s => s.OwnerUserId == userId).ToListAsync());
        Assert.Single(await _db.RustPlusPairings.Where(p => p.UserId == userId).ToListAsync());
    }

    [Fact]
    public async Task DifferentUsers_SameIp_GetSeparateServers()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await _handler.HandleAsync(userA, MakeNotification(ip: "203.0.113.9"), CancellationToken.None);
        await _handler.HandleAsync(userB, MakeNotification(ip: "203.0.113.9"), CancellationToken.None);

        Assert.Single(await _db.RustServers.Where(s => s.OwnerUserId == userA).ToListAsync());
        Assert.Single(await _db.RustServers.Where(s => s.OwnerUserId == userB).ToListAsync());
    }

    [Fact]
    public async Task PairingNotification_IsRaised()
    {
        var userId = Guid.NewGuid();

        await _handler.HandleAsync(userId, MakeNotification(name: "Rustex EU"), CancellationToken.None);

        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.UserId == userId && n.Type == "rustplus.server_paired");
        Assert.NotNull(notif);
        Assert.Contains("Rustex EU", notif.Title);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connectionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
