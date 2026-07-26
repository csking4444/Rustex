namespace Rustex.Infrastructure.Auth;

public class SteamAuthOptions
{
    public const string SectionName = "Steam";

    /// <summary>Set false to hide/disable Steam login entirely. Steam login does NOT need
    /// <see cref="ApiKey"/> — OpenID needs no key at all; only the optional profile lookup does.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Free key from https://steamcommunity.com/dev/apikey — used only to fetch the
    /// public profile (persona name, avatar) after a login is verified. Leave empty and users
    /// still sign in fine; they just get a placeholder "SteamUser123456" username they can
    /// rename, instead of their real Steam name.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Must exactly match the origin the browser actually reaches this API on — Steam
    /// signs `return_to` and the callback rejects any mismatch (see
    /// <see cref="Rustex.Domain.Abstractions.ISteamAuthService.VerifyAsync"/>).</summary>
    public string ReturnUrl { get; set; } = "https://localhost:5443/api/auth/steam/callback";
    public string Realm { get; set; } = "https://localhost:5443";
    public string FrontendCallbackUrl { get; set; } = "http://localhost:5173/auth/callback";
}
