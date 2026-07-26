namespace Rustex.Domain.Abstractions;

/// <summary>Steam login via OpenID 2.0 (steamcommunity.com/openid) — the same mechanism Steam
/// itself has offered third-party sites for years. Unlike Rust+, this is a fully public,
/// documented protocol, so this is a real implementation: build the authorize URL, verify the
/// signed callback by re-posting it back to Steam (`check_authentication`), then look up the
/// profile via the Steam Web API (requires a free API key from steamcommunity.com/dev/apikey).</summary>
public interface ISteamAuthService
{
    bool IsConfigured { get; }

    string BuildAuthorizeUrl(string returnUrl, string realm);

    /// <summary>Returns the verified SteamID64, or null if the callback's signature doesn't
    /// check out (Steam says the assertion is invalid, or a param is missing/malformed).</summary>
    Task<string?> VerifyAndGetSteamIdAsync(IReadOnlyDictionary<string, string> callbackQuery, CancellationToken ct);

    Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId64, CancellationToken ct);
}

public sealed record SteamPlayerSummary(string SteamId, string PersonaName, string AvatarUrl);
