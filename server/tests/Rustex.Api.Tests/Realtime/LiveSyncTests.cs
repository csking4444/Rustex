using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rustex.Api.Auth;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.Realtime;
using Xunit;

namespace Rustex.Api.Tests.Realtime;

public class LiveScopeTests
{
    [Theory]
    [InlineData("server:8a1f4c2e-0000-4000-8000-000000000001", true)]
    [InlineData("user:8a1f4c2e-0000-4000-8000-000000000001", true)]
    public void TryParse_AcceptsKnownKinds(string raw, bool expected)
    {
        Assert.Equal(expected, LiveScope.TryParse(raw, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("server")]
    [InlineData("server:not-a-guid")]
    // An unknown kind must be refused rather than parsed — a new scope type has to opt in to
    // authorization deliberately, not inherit access because the parser was permissive.
    [InlineData("admin:8a1f4c2e-0000-4000-8000-000000000001")]
    [InlineData("../server:8a1f4c2e-0000-4000-8000-000000000001")]
    public void TryParse_RejectsAnythingElse(string? raw)
    {
        Assert.False(LiveScope.TryParse(raw, out _));
    }

    [Fact]
    public void ToString_RoundTripsThroughTryParse()
    {
        var original = LiveScope.Server(Guid.NewGuid());

        Assert.True(LiveScope.TryParse(original.ToString(), out var parsed));
        Assert.Equal(original, parsed);
    }
}

/// <summary>Covers the access check that hub group membership depends on. Without it, any signed-in
/// user could subscribe to another account's server id and receive their live data.</summary>
public class LiveScopeAuthorizerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly LiveScopeAuthorizer _authorizer;
    private readonly User _owner;
    private readonly User _stranger;
    private readonly RustServer _server;

    public LiveScopeAuthorizerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _owner = new User { Username = "owner" };
        _stranger = new User { Username = "stranger" };
        _db.Users.AddRange(_owner, _stranger);
        _db.SaveChanges();

        _server = new RustServer
        {
            OwnerUserId = _owner.Id,
            Name = "Test server",
            IpAddress = "127.0.0.1",
            GamePort = 28015,
        };
        _db.RustServers.Add(_server);
        _db.SaveChanges();

        _authorizer = new LiveScopeAuthorizer(_db);
    }

    [Fact]
    public async Task Owner_MayAccessTheirServer()
    {
        Assert.True(await _authorizer.CanAccessAsync(_owner.Id, LiveScope.Server(_server.Id), default));
    }

    [Fact]
    public async Task Stranger_MayNotAccessSomeoneElsesServer()
    {
        Assert.False(await _authorizer.CanAccessAsync(_stranger.Id, LiveScope.Server(_server.Id), default));
    }

    [Fact]
    public async Task NonexistentServer_IsRefused()
    {
        Assert.False(await _authorizer.CanAccessAsync(_owner.Id, LiveScope.Server(Guid.NewGuid()), default));
    }

    [Fact]
    public async Task UserScope_IsOnlyEverYourOwn()
    {
        Assert.True(await _authorizer.CanAccessAsync(_owner.Id, LiveScope.User(_owner.Id), default));
        Assert.False(await _authorizer.CanAccessAsync(_owner.Id, LiveScope.User(_stranger.Id), default));
    }

    [Fact]
    public async Task UnknownScopeKind_IsRefused()
    {
        Assert.False(await _authorizer.CanAccessAsync(_owner.Id, new LiveScope("admin", _owner.Id), default));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}

public class SyncRetryTests
{
    [Fact]
    public void Backoff_GrowsThenCaps()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), SyncRetryWorker.BackoffFor(1));
        Assert.Equal(TimeSpan.FromSeconds(2), SyncRetryWorker.BackoffFor(2));
        Assert.Equal(TimeSpan.FromSeconds(4), SyncRetryWorker.BackoffFor(3));
        Assert.Equal(TimeSpan.FromSeconds(16), SyncRetryWorker.BackoffFor(5));
        // Capped: a long outage must not push a retry so far out that the state is worthless.
        Assert.Equal(TimeSpan.FromSeconds(16), SyncRetryWorker.BackoffFor(50));
    }

    [Fact]
    public async Task BroadcastFailure_QueuesARetry()
    {
        var store = new FakeLiveStore();
        var broadcaster = new FakeBroadcaster { Fail = true };
        var queue = new SyncRetryQueue();
        var publisher = new LiveSyncPublisher(store, broadcaster, queue, NullLogger<LiveSyncPublisher>.Instance);

        await publisher.PublishAsync(LiveScope.Server(Guid.NewGuid()), LiveSections.Status, new { Players = 12 }, default);

        // The cache write still succeeded, so a reconnecting client would be correct regardless.
        Assert.Single(store.Writes);
        Assert.True(queue.Channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task StoreFailure_QueuesARetryAndSkipsTheBroadcast()
    {
        var store = new FakeLiveStore { Fail = true };
        var broadcaster = new FakeBroadcaster();
        var queue = new SyncRetryQueue();
        var publisher = new LiveSyncPublisher(store, broadcaster, queue, NullLogger<LiveSyncPublisher>.Instance);

        await publisher.PublishAsync(LiveScope.Server(Guid.NewGuid()), LiveSections.Team, new { Members = 3 }, default);

        Assert.Empty(broadcaster.Sent);
        Assert.True(queue.Channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task SuccessfulPublish_QueuesNothing()
    {
        var store = new FakeLiveStore();
        var broadcaster = new FakeBroadcaster();
        var queue = new SyncRetryQueue();
        var publisher = new LiveSyncPublisher(store, broadcaster, queue, NullLogger<LiveSyncPublisher>.Instance);

        await publisher.PublishAsync(LiveScope.Server(Guid.NewGuid()), LiveSections.Status, new { Ping = 42 }, default);

        Assert.Single(broadcaster.Sent);
        Assert.False(queue.Channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task PublishedVersion_IsCarriedToTheClient()
    {
        var store = new FakeLiveStore { NextVersion = 7 };
        var broadcaster = new FakeBroadcaster();
        var queue = new SyncRetryQueue();
        var publisher = new LiveSyncPublisher(store, broadcaster, queue, NullLogger<LiveSyncPublisher>.Instance);

        await publisher.PublishAsync(LiveScope.Server(Guid.NewGuid()), LiveSections.Status, new { Ping = 1 }, default);

        // Clients use this to spot a gap and re-fetch, so it has to survive the trip.
        Assert.Equal(7, broadcaster.Sent[0].Version);
    }

    private sealed class FakeLiveStore : ILiveStateStore
    {
        public bool Fail { get; init; }
        public long NextVersion { get; set; } = 1;
        public List<(LiveScope Scope, string Section)> Writes { get; } = [];

        public Task<long> SetSectionAsync(LiveScope scope, string section, object payload, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("redis down");
            Writes.Add((scope, section));
            return Task.FromResult(NextVersion);
        }

        public Task<LiveSnapshot?> GetSnapshotAsync(LiveScope scope, CancellationToken ct) =>
            Task.FromResult<LiveSnapshot?>(new LiveSnapshot(
                scope.ToString(), NextVersion, DateTimeOffset.UtcNow, new Dictionary<string, JsonElement>()));

        public Task ClearAsync(LiveScope scope, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeBroadcaster : ILiveBroadcaster
    {
        public bool Fail { get; init; }
        public List<LiveUpdate> Sent { get; } = [];

        public Task BroadcastAsync(LiveScope scope, LiveUpdate update, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("hub unavailable");
            Sent.Add(update);
            return Task.CompletedTask;
        }
    }
}
