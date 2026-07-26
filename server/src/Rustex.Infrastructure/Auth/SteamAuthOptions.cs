namespace Rustex.Infrastructure.Auth;

public class SteamAuthOptions
{
    public const string SectionName = "Steam";

    /// <summary>Free key from https://steamcommunity.com/dev/apikey — used only to fetch the
    /// public profile (persona name, avatar) after a login is verified. Leave empty to disable
    /// Steam login entirely (ISteamAuthService.IsConfigured gates on this).</summary>
    public string? ApiKey { get; set; }

    public string ReturnUrl { get; set; } = "https://localhost:5443/api/auth/steam/callback";
    public string Realm { get; set; } = "https://localhost:5443";
    public string FrontendCallbackUrl { get; set; } = "http://localhost:5173/auth/callback";
}
