using System.Text.Json;
using StackExchange.Redis;

namespace Rustex.Infrastructure.Caching;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null);
    Task RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);
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
}
