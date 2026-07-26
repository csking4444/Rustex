namespace Rustex.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "rustex";
    public string Audience { get; set; } = "rustex-client";
    public string SigningKey { get; set; } = default!;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public class DiscordOAuthOptions
{
    public const string SectionName = "Discord";

    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string RedirectUri { get; set; } = default!;
    public string FrontendCallbackUrl { get; set; } = default!;
}
