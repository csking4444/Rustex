using System.Text.Json;
using Rustex.Domain.Abstractions;
using StackExchange.Redis;

namespace Rustex.Infrastructure.Realtime;

/// <summary>Redis-backed live state, one hash per scope with one field per section.
///
/// A hash rather than a single serialised blob because producers are independent: the status
/// poller and the team tracker write different sections concurrently, and read-modify-write on a
/// shared blob would lose whichever update landed second.
///
/// Uses <see cref="IConnectionMultiplexer"/> directly rather than IRedisCacheService — that
/// interface is string-key/value only and hash semantics are the whole point here.</summary>
public sealed class RedisLiveStateStore : ILiveStateStore
{
    /// <summary>Snapshots are a cache of state we can always rebuild from the game server, so they
    /// expire rather than accumulating for every server that was ever paired. Refreshed on each
    /// write, so an actively-updating scope never expires out from under a connected client.</summary>
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromHours(6);

    private const string VersionField = "__version";
    private const string UpdatedField = "__updated";

    private readonly IConnectionMultiplexer _redis;

    public RedisLiveStateStore(IConnectionMultiplexer redis) => _redis = redis;

    private IDatabase Db => _redis.GetDatabase();

    private static string Key(LiveScope scope) => $"live:{scope}";

    public async Task<long> SetSectionAsync(LiveScope scope, string section, object payload, CancellationToken ct)
    {
        var key = Key(scope);
        var json = JsonSerializer.Serialize(payload);

        // HashIncrement is atomic, so two producers writing different sections at the same moment
        // still get distinct, strictly increasing versions — which is what lets a client detect a
        // gap and know it needs a fresh snapshot.
        var version = await Db.HashIncrementAsync(key, VersionField);

        await Db.HashSetAsync(key,
        [
            new HashEntry(section, json),
            new HashEntry(UpdatedField, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        ]);

        await Db.KeyExpireAsync(key, SnapshotTtl);
        return version;
    }

    public async Task<LiveSnapshot?> GetSnapshotAsync(LiveScope scope, CancellationToken ct)
    {
        var entries = await Db.HashGetAllAsync(Key(scope));
        if (entries.Length == 0) return null;

        long version = 0;
        var updated = DateTimeOffset.UtcNow;
        var sections = new Dictionary<string, JsonElement>();

        foreach (var entry in entries)
        {
            var name = entry.Name.ToString();

            if (name == VersionField)
            {
                version = (long)entry.Value;
                continue;
            }
            if (name == UpdatedField)
            {
                updated = DateTimeOffset.FromUnixTimeMilliseconds((long)entry.Value);
                continue;
            }

            try
            {
                // Explicit string cast: RedisValue converts implicitly to both string and
                // ReadOnlyMemory<byte>, which makes the JsonDocument.Parse overload ambiguous.
                sections[name] = JsonDocument.Parse((string)entry.Value!).RootElement.Clone();
            }
            catch (JsonException)
            {
                // A section written by an older build with an incompatible shape should not take
                // the whole snapshot down — skip it and let the next publish overwrite it.
            }
        }

        return new LiveSnapshot(scope.ToString(), version, updated, sections);
    }

    public Task ClearAsync(LiveScope scope, CancellationToken ct) => Db.KeyDeleteAsync(Key(scope));
}
