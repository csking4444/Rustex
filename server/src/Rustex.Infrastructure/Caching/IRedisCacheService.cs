using System.Text.Json;
using StackExchange.Redis;

namespace Rustex.Infrastructure.Caching;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null);
    Task RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);

    /// <summary>Sets the key only if it does not already exist, returning true if this caller won.
    /// Use this instead of <see cref="ExistsAsync"/> followed by <see cref="SetAsync{T}"/> — that
    /// pair is a check-then-act race, which is exactly what single-use tokens (OpenID replay
    /// nonces, distributed ownership locks) must not have.</summary>
    Task<bool> TrySetIfAbsentAsync<T>(string key, T value, TimeSpan ttl);

    /// <summary>Atomically reads and deletes a key, so a value can be consumed exactly once even
    /// if two requests race for it.</summary>
    Task<T?> GetAndDeleteAsync<T>(string key);
}

public class RedisCacheService : IRedisCacheService
{
    private readonly IConnectionMultiplexer _redis;

    public RedisCacheService(IConnectionMultiplexer redis) => _redis = redis;

    private IDatabase Db => _redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key)
    {
        var value = await Db.StringGetAsync(key);
        if (!value.HasValue) return default;
        return JsonSerializer.Deserialize<T>(value!);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        var json = JsonSerializer.Serialize(value);
        return Db.StringSetAsync(key, json, ttl);
    }

    public Task RemoveAsync(string key) => Db.KeyDeleteAsync(key);

    public Task<bool> ExistsAsync(string key) => Db.KeyExistsAsync(key);

    public Task<bool> TrySetIfAbsentAsync<T>(string key, T value, TimeSpan ttl)
    {
        var json = JsonSerializer.Serialize(value);
        return Db.StringSetAsync(key, json, ttl, When.NotExists);
    }

    public async Task<T?> GetAndDeleteAsync<T>(string key)
    {
        var value = await Db.StringGetDeleteAsync(key);
        if (!value.HasValue) return default;
        return JsonSerializer.Deserialize<T>(value!);
    }
}
