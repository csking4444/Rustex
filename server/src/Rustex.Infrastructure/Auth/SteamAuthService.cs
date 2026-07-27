using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Rustex.Domain.Abstractions;

namespace Rustex.Infrastructure.Auth;

public class SteamAuthService : ISteamAuthService
{
    private const string OpenIdEndpoint = "https://steamcommunity.com/openid/login";
    private const string OpenIdPath = "/openid/login";
    // Steam's sign-in form itself. /openid/login auto-approves and redirects straight back when the
    // browser already has a Steam session (correct SSO behaviour, but it reads as "the button did
    // nothing"), so forceLogin points at the form and passes the OpenID request along as ?goto=.
    private const string LoginFormEndpoint = "https://steamcommunity.com/openid/loginform/";
    private const string OpenIdNamespace = "http://specs.openid.net/auth/2.0";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";
    private const string SteamIdentityPrefix = "https://steamcommunity.com/openid/id/";

    // The fields that actually matter if stripped from openid.signed. Steam's check_authentication
    // only vouches for the fields listed there — it does NOT re-derive which fields *should* have
    // been signed. Without this check, an attacker who can get one valid Steam assertion (e.g. for
    // their own account) can drop e.g. `claimed_id` from `openid.signed`, substitute a different
    // claimed_id in the payload, and Steam will still answer is_valid:true because it only verifies
    // the fields still listed as signed.
    private static readonly string[] RequiredSignedFields =
        ["op_endpoint", "return_to", "claimed_id", "identity", "response_nonce", "assoc_handle"];

    private static readonly TimeSpan NonceSkew = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly SteamAuthOptions _options;

    public SteamAuthService(HttpClient http, IOptions<SteamAuthOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public bool IsConfigured =>
        _options.Enabled && !string.IsNullOrWhiteSpace(_options.ReturnUrl) && !string.IsNullOrWhiteSpace(_options.Realm);

    public bool HasProfileApi => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string BuildAuthorizeUrl(string returnUrl, string realm, string state, bool forceLogin = false)
    {
        var returnToWithState = AppendQueryParam(returnUrl, "state", state);
        var query = new Dictionary<string, string>
        {
            ["openid.ns"] = OpenIdNamespace,
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnToWithState,
            ["openid.realm"] = realm,
            ["openid.identity"] = IdentifierSelect,
            ["openid.claimed_id"] = IdentifierSelect,
        };

        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        if (!forceLogin) return $"{OpenIdEndpoint}?{qs}";

        // goto is a steamcommunity.com-relative path, which is the shape Steam's own redirect chain
        // uses — the whole OpenID request rides along inside it and resumes after the user signs in.
        return $"{LoginFormEndpoint}?goto={Uri.EscapeDataString($"{OpenIdPath}?{qs}")}";
    }

    public async Task<SteamVerificationResult?> VerifyAsync(IReadOnlyDictionary<string, string> callbackQuery, CancellationToken ct)
    {
        if (!callbackQuery.TryGetValue("openid.mode", out var mode) || mode != "id_res") return null;

        if (!callbackQuery.TryGetValue("openid.op_endpoint", out var opEndpoint) ||
            !string.Equals(opEndpoint, OpenIdEndpoint, StringComparison.Ordinal))
            return null;

        if (!callbackQuery.TryGetValue("openid.signed", out var signedList)) return null;
        var signedFields = signedList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (RequiredSignedFields.Any(required => !signedFields.Contains(required, StringComparer.Ordinal)))
            return null;

        if (!callbackQuery.TryGetValue("openid.claimed_id", out var claimedId)) return null;
        if (!callbackQuery.TryGetValue("openid.identity", out var identity)) return null;
        if (!string.Equals(claimedId, identity, StringComparison.Ordinal)) return null;
        if (!claimedId.StartsWith(SteamIdentityPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        if (!callbackQuery.TryGetValue("openid.return_to", out var returnTo)) return null;
        if (!ReturnToMatchesConfigured(returnTo)) return null;

        if (!callbackQuery.TryGetValue("openid.response_nonce", out var responseNonce)) return null;
        if (!IsNonceTimestampFresh(responseNonce)) return null;

        // Re-post the exact assertion back to Steam with mode swapped to check_authentication —
        // this is how OpenID 2.0 verification works: Steam tells us whether the signed response
        // we received genuinely came from it, so a forged callback request can't fake a login.
        var verificationForm = callbackQuery.ToDictionary(kv => kv.Key, kv => kv.Value);
        verificationForm["openid.mode"] = "check_authentication";

        using var content = new FormUrlEncodedContent(verificationForm);
        using var response = await _http.PostAsync(OpenIdEndpoint, content, ct);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!body.Contains("is_valid:true", StringComparison.Ordinal)) return null;

        var steamId64 = claimedId.TrimEnd('/').Split('/')[^1];
        if (!long.TryParse(steamId64, out _)) return null;

        var state = ExtractQueryParam(returnTo, "state") ?? "";
        return new SteamVerificationResult(steamId64, state, responseNonce);
    }

    public async Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId64, CancellationToken ct)
    {
        if (!HasProfileApi) return null;

        var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/" +
                   $"?key={Uri.EscapeDataString(_options.ApiKey!)}&steamids={Uri.EscapeDataString(steamId64)}";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: ct);

        if (!doc.TryGetProperty("response", out var responseEl) || !responseEl.TryGetProperty("players", out var players) || players.GetArrayLength() == 0)
            return null;

        var player = players[0];
        return new SteamPlayerSummary(
            player.GetProperty("steamid").GetString()!,
            player.GetProperty("personaname").GetString()!,
            player.GetProperty("avatarfull").GetString()!);
    }

    private bool ReturnToMatchesConfigured(string returnTo)
    {
        if (!Uri.TryCreate(returnTo, UriKind.Absolute, out var actual)) return false;
        if (!Uri.TryCreate(_options.ReturnUrl, UriKind.Absolute, out var expected)) return false;
        return actual.Scheme == expected.Scheme && actual.Authority == expected.Authority && actual.AbsolutePath == expected.AbsolutePath;
    }

    private static bool IsNonceTimestampFresh(string responseNonce)
    {
        // Format is an ISO-8601 UTC timestamp immediately followed by a random suffix, e.g.
        // "2026-07-26T12:00:00ZAbCdEf123" — the timestamp portion is exactly 20 characters.
        if (responseNonce.Length < 20) return false;
        var timestampPart = responseNonce[..20];
        if (!DateTimeOffset.TryParse(timestampPart, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            return false;

        var skew = DateTimeOffset.UtcNow - timestamp;
        return skew > -NonceSkew && skew < NonceSkew;
    }

    private static string AppendQueryParam(string url, string key, string value)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    private static string? ExtractQueryParam(string url, string key)
    {
        var queryStart = url.IndexOf('?');
        if (queryStart < 0) return null;

        foreach (var pair in url[(queryStart + 1)..].Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == key) return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}
