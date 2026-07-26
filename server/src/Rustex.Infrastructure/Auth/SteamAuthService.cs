using System.Text.Json;
using Microsoft.Extensions.Options;
using Rustex.Domain.Abstractions;

namespace Rustex.Infrastructure.Auth;

public class SteamAuthService : ISteamAuthService
{
    private const string OpenIdEndpoint = "https://steamcommunity.com/openid/login";
    private const string OpenIdNamespace = "http://specs.openid.net/auth/2.0";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";

    private readonly HttpClient _http;
    private readonly SteamAuthOptions _options;

    public SteamAuthService(HttpClient http, IOptions<SteamAuthOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string BuildAuthorizeUrl(string returnUrl, string realm)
    {
        var query = new Dictionary<string, string>
        {
            ["openid.ns"] = OpenIdNamespace,
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnUrl,
            ["openid.realm"] = realm,
            ["openid.identity"] = IdentifierSelect,
            ["openid.claimed_id"] = IdentifierSelect,
        };

        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{OpenIdEndpoint}?{qs}";
    }

    public async Task<string?> VerifyAndGetSteamIdAsync(IReadOnlyDictionary<string, string> callbackQuery, CancellationToken ct)
    {
        if (!callbackQuery.TryGetValue("openid.mode", out var mode) || mode != "id_res") return null;
        if (!callbackQuery.TryGetValue("openid.claimed_id", out var claimedId)) return null;
        if (!claimedId.StartsWith("https://steamcommunity.com/openid/id/", StringComparison.OrdinalIgnoreCase)) return null;

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
        return long.TryParse(steamId64, out _) ? steamId64 : null;
    }

    public async Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId64, CancellationToken ct)
    {
        if (!IsConfigured) return null;

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
}
