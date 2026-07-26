using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Rustex.Infrastructure.Auth;

public interface IDiscordOAuthService
{
    string BuildAuthorizeUrl(string state);
    Task<DiscordTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct);
    Task<DiscordUserInfo> GetUserInfoAsync(string accessToken, CancellationToken ct);
}

public sealed record DiscordTokenResponse(string AccessToken, string TokenType, int ExpiresIn, string? RefreshToken, string Scope);
public sealed record DiscordUserInfo(string Id, string Username, string? Avatar, string? Email);

public class DiscordOAuthService : IDiscordOAuthService
{
    private const string ApiBase = "https://discord.com/api/v10";
    private readonly HttpClient _http;
    private readonly DiscordOAuthOptions _options;

    public DiscordOAuthService(HttpClient http, IOptions<DiscordOAuthOptions> options)
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
            ["scope"] = "identify email",
            ["state"] = state,
        };
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"https://discord.com/oauth2/authorize?{qs}";
    }

    public async Task<DiscordTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
        };

        using var response = await _http.PostAsync($"{ApiBase}/oauth2/token", new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var json = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: ct);

        return new DiscordTokenResponse(
            json.GetProperty("access_token").GetString()!,
            json.GetProperty("token_type").GetString()!,
            json.GetProperty("expires_in").GetInt32(),
            json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            json.GetProperty("scope").GetString()!);
    }

    public async Task<DiscordUserInfo> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/users/@me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var json = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: ct);

        return new DiscordUserInfo(
            json.GetProperty("id").GetString()!,
            json.GetProperty("username").GetString()!,
            json.TryGetProperty("avatar", out var a) ? a.GetString() : null,
            json.TryGetProperty("email", out var e) ? e.GetString() : null);
    }
}
