using System.Security.Cryptography;
using Rustex.Infrastructure.Caching;

namespace Rustex.Infrastructure.Auth;

/// <summary>What a Steam login redirect is *for* — a fresh account login, or attaching Steam to
/// an already-signed-in user's existing account. OpenID 2.0 has no `state` parameter of its own,
/// so this whole record travels as the nonce embedded in `openid.return_to`, which is itself
/// inside Steam's signed field set — an attacker can't substitute a different nonce without
/// breaking the signature.</summary>
public enum SteamAuthPurpose { Login, Link }

public sealed record SteamOpenIdState(string Nonce, SteamAuthPurpose Purpose, Guid? LinkUserId, DateTimeOffset CreatedAt);

public interface ISteamOpenIdStateStore
{
    Task<string> IssueAsync(SteamAuthPurpose purpose, Guid? linkUserId = null);

    /// <summary>Single-use: returns the state once, then it's gone. A replayed callback URL
    /// (or two tabs racing the same login) gets null the second time.</summary>
    Task<SteamOpenIdState?> ConsumeAsync(string nonce);

    /// <summary>Separate from <see cref="ConsumeAsync"/> — this guards Steam's own
    /// `openid.response_nonce`, so a captured, fully-valid callback URL can't be replayed after
    /// our nonce has already been consumed by the real request. Returns true the first time a
    /// given response_nonce is seen, false on every replay.</summary>
    Task<bool> MarkNonceUsedOnceAsync(string responseNonce);
}

/// <summary>Redis-backed rather than cookie-only so it survives multiple API replicas and still
/// works if a browser's third-party/SameSite cookie policy is stricter than expected. The
/// existing rw_oauth_state cookie used by Discord/Google stays as a secondary defence for those
/// flows; Steam has no cookie equivalent to pair with since the nonce has to survive inside a
/// URL Steam itself redirects through.</summary>
public sealed class SteamOpenIdStateStore : ISteamOpenIdStateStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly IRedisCacheService _cache;

    public SteamOpenIdStateStore(IRedisCacheService cache) => _cache = cache;

    public async Task<string> IssueAsync(SteamAuthPurpose purpose, Guid? linkUserId = null)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var state = new SteamOpenIdState(nonce, purpose, linkUserId, DateTimeOffset.UtcNow);
        await _cache.SetAsync(Key(nonce), state, Ttl);
        return nonce;
    }

    public Task<SteamOpenIdState?> ConsumeAsync(string nonce) => _cache.GetAndDeleteAsync<SteamOpenIdState>(Key(nonce));

    public Task<bool> MarkNonceUsedOnceAsync(string responseNonce) =>
        // The ±5-minute skew check on the nonce's own timestamp (SteamAuthService) bounds how
        // long a replay window could matter, so a 10-minute TTL here is already generous cleanup.
        _cache.TrySetIfAbsentAsync($"steam:openid:nonce:{responseNonce}", 1, Ttl);

    private static string Key(string nonce) => $"steam:openid:state:{nonce}";
}
