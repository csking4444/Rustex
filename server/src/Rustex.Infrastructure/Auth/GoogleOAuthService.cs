using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Rustex.Infrastructure.Auth;

public interface IGoogleOAuthService
{
    string BuildAuthorizeUrl(string state);
    Task<GoogleTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct);
    Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken ct);
}

public sealed record GoogleTokenResponse(string AccessToken, string TokenType, int ExpiresIn, string? IdToken);
public sealed record GoogleUserInfo(string Sub, string? Email, bool EmailVerified, string? Name, string? Picture);

/// <summary>Standard OAuth2/OIDC "Sign in with Google" — a fully public, stable, well-documented
/// protocol (unlike Rust+), so this is a real implementation. Requires a Google Cloud OAuth
/// client (console.cloud.google.com -> APIs & Services -> Credentials), same setup shape as the
/// Discord app.</summary>
public class GoogleOAuthService : IGoogleOAuthService
{
    private readonly HttpClient _http;
    private readonly GoogleOAuthOptions _options;

    public GoogleOAuthService(HttpClient http, IOptions<GoogleOAuthOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string BuildAuthorizeUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["access_type"] = "online",
            ["prompt"] = "select_account",
        };
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"https://accounts.google.com/o/oauth2/v2/auth?{qs}";
    }

    public async Task<GoogleTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
        };

        using var response = await _http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var json = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: ct);

        return new GoogleTokenResponse(
            json.GetProperty("access_token").GetString()!,
            json.GetProperty("token_type").GetString()!,
            json.GetProperty("expires_in").GetInt32(),
            json.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null);
    }

    public async Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var json = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: ct);

        return new GoogleUserInfo(
            json.GetProperty("sub").GetString()!,
            json.TryGetProperty("email", out var e) ? e.GetString() : null,
            json.TryGetProperty("email_verified", out var ev) && ev.ValueKind == JsonValueKind.True,
            json.TryGetProperty("name", out var n) ? n.GetString() : null,
            json.TryGetProperty("picture", out var p) ? p.GetString() : null);
    }
}
