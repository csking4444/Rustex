namespace Rustex.Domain.Abstractions;

/// <summary>Steam login via OpenID 2.0 (steamcommunity.com/openid) — the same mechanism Steam
/// itself has offered third-party sites for years. Unlike Rust+, this is a fully public,
/// documented protocol, so this is a real implementation: build the authorize URL, verify the
/// signed callback by re-posting it back to Steam (`check_authentication`) plus the full set of
/// OpenID 2.0 §11 checks (op_endpoint, signed-field coverage, claimed_id/identity match,
/// return_to match, nonce freshness), then look up the profile via the Steam Web API (requires a
/// free API key from steamcommunity.com/dev/apikey — optional, only used for the display name and
/// avatar).</summary>
public interface ISteamAuthService
{
    /// <summary>True when Steam login can be offered at all (enabled + return URL/realm set).
    /// Does NOT require an API key — OpenID login itself needs no key.</summary>
    bool IsConfigured { get; }

    /// <summary>True when the optional Steam Web API key is set, so <see cref="GetPlayerSummaryAsync"/>
    /// can fetch a real display name/avatar instead of leaving the caller to fall back to a
    /// placeholder username.</summary>
    bool HasProfileApi { get; }

    /// <summary>Builds the URL to redirect the browser to. <paramref name="state"/> is a
    /// single-use nonce from <see cref="Rustex.Infrastructure.Auth.ISteamOpenIdStateStore"/>,
    /// embedded in the returned `return_to` so it round-trips inside Steam's signed assertion.
    /// <paramref name="forceLogin"/> targets Steam's sign-in form instead of the auto-approving
    /// OpenID endpoint, so a user with an existing Steam session still gets prompted — needed for
    /// "sign in as someone else", since OpenID 2.0 has no `prompt=login` equivalent.</summary>
    string BuildAuthorizeUrl(string returnUrl, string realm, string state, bool forceLogin = false);

    /// <summary>Verifies a Steam OpenID 2.0 callback in full: re-posts the assertion to Steam for
    /// `check_authentication`, and additionally validates `op_endpoint`, that `openid.signed`
    /// actually covers the fields that matter (an attacker can otherwise strip a field from the
    /// signed set and Steam still answers `is_valid:true` for the reduced set), that
    /// `claimed_id == identity`, that `return_to` matches what we sent, and that
    /// `response_nonce`'s embedded timestamp is within a 5-minute skew window. Returns null if
    /// any check fails.</summary>
    Task<SteamVerificationResult?> VerifyAsync(IReadOnlyDictionary<string, string> callbackQuery, CancellationToken ct);

    Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId64, CancellationToken ct);
}

/// <summary>ReturnToState is the nonce we embedded in return_to, extracted back out so the caller
/// can look up which SteamOpenIdState (login vs. link, and for whom) this callback belongs to.
/// ResponseNonce is Steam's own nonce, used for replay protection — a captured callback URL must
/// not be usable twice.</summary>
public sealed record SteamVerificationResult(string SteamId64, string ReturnToState, string ResponseNonce);

public sealed record SteamPlayerSummary(string SteamId, string PersonaName, string AvatarUrl);
