using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rustex.Api.Dtos;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Auth;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string StateCookieName = "rw_oauth_state";

    private readonly IDiscordOAuthService _discord;
    private readonly IGoogleOAuthService _google;
    private readonly ISteamAuthService _steam;
    private readonly IPasswordAuthService _passwordAuth;
    private readonly IJwtTokenService _jwt;
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwtOptions;
    private readonly DiscordOAuthOptions _discordOptions;
    private readonly GoogleOAuthOptions _googleOptions;
    private readonly SteamAuthOptions _steamOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IDiscordOAuthService discord,
        IGoogleOAuthService google,
        ISteamAuthService steam,
        IPasswordAuthService passwordAuth,
        IJwtTokenService jwt,
        AppDbContext db,
        IOptions<JwtOptions> jwtOptions,
        IOptions<DiscordOAuthOptions> discordOptions,
        IOptions<GoogleOAuthOptions> googleOptions,
        IOptions<SteamAuthOptions> steamOptions,
        ILogger<AuthController> logger)
    {
        _discord = discord;
        _google = google;
        _steam = steam;
        _passwordAuth = passwordAuth;
        _jwt = jwt;
        _db = db;
        _jwtOptions = jwtOptions.Value;
        _discordOptions = discordOptions.Value;
        _googleOptions = googleOptions.Value;
        _steamOptions = steamOptions.Value;
        _logger = logger;
    }

    // ---------- Email + password ----------

    [HttpPost("register")]
    public async Task<ActionResult<TokenResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var username = request.Username.Trim();

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest("A valid email address is required.");
        if (string.IsNullOrWhiteSpace(username) || username.Length < 2)
            return BadRequest("Username must be at least 2 characters.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return BadRequest("Password must be at least 8 characters.");

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict("An account with that email already exists.");

        var user = new User
        {
            Email = email,
            Username = username,
            PasswordHash = _passwordAuth.HashPassword(request.Password),
            LastLoginAt = DateTimeOffset.UtcNow,
        };

        _db.Users.Add(user);
        _db.UserProfiles.Add(new UserProfile { UserId = user.Id, DisplayName = username });
        _db.UserSettings.Add(new UserSettings { UserId = user.Id });
        await _db.SaveChangesAsync(ct);

        return Ok(await IssueTokenPairAsync(user, ct));
    }

    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user?.PasswordHash is null || !_passwordAuth.VerifyPassword(user.PasswordHash, request.Password))
            return Unauthorized("Invalid email or password.");

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(await IssueTokenPairAsync(user, ct));
    }

    // ---------- Discord OAuth2 ----------

    [HttpGet("discord/login")]
    public IActionResult DiscordLogin()
    {
        var state = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10),
        });

        return Redirect(_discord.BuildAuthorizeUrl(state));
    }

    [HttpGet("discord/callback")]
    public async Task<IActionResult> DiscordCallback([FromQuery] string code, [FromQuery] string state, CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(StateCookieName, out var expectedState) || expectedState != state)
            return BadRequest("Invalid OAuth state.");

        Response.Cookies.Delete(StateCookieName);

        var tokenResponse = await _discord.ExchangeCodeAsync(code, ct);
        var discordUser = await _discord.GetUserInfoAsync(tokenResponse.AccessToken, ct);
        var avatarUrl = discordUser.Avatar is not null
            ? $"https://cdn.discordapp.com/avatars/{discordUser.Id}/{discordUser.Avatar}.png"
            : null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.DiscordId == discordUser.Id, ct);

        if (user is null)
        {
            user = new User
            {
                DiscordId = discordUser.Id,
                Username = discordUser.Username,
                AvatarUrl = avatarUrl,
                Email = discordUser.Email,
                LastLoginAt = DateTimeOffset.UtcNow,
            };
            _db.Users.Add(user);
            _db.UserProfiles.Add(new UserProfile { UserId = user.Id, DisplayName = discordUser.Username });
            _db.UserSettings.Add(new UserSettings { UserId = user.Id });
        }
        else
        {
            user.Username = discordUser.Username;
            user.AvatarUrl = avatarUrl;
            user.Email = discordUser.Email ?? user.Email;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            user.LastLoginAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        var tokens = await IssueTokenPairAsync(user, ct);
        return Redirect(BuildFrontendRedirect(_discordOptions.FrontendCallbackUrl, tokens));
    }

    // ---------- Google OAuth2 ----------

    [HttpGet("google/login")]
    public IActionResult GoogleLogin()
    {
        var state = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10),
        });

        return Redirect(_google.BuildAuthorizeUrl(state));
    }

    [HttpGet("google/callback")]
    public async Task<IActionResult> GoogleCallback([FromQuery] string code, [FromQuery] string state, CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(StateCookieName, out var expectedState) || expectedState != state)
            return BadRequest("Invalid OAuth state.");

        Response.Cookies.Delete(StateCookieName);

        var tokenResponse = await _google.ExchangeCodeAsync(code, ct);
        var googleUser = await _google.GetUserInfoAsync(tokenResponse.AccessToken, ct);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GoogleId == googleUser.Sub, ct);
        var displayName = googleUser.Name ?? googleUser.Email ?? $"GoogleUser{googleUser.Sub[^6..]}";

        if (user is null)
        {
            // A verified Google email matching an existing password/other-provider account
            // links to it instead of creating a duplicate — Google confirms email ownership,
            // so this is safe (unlike blindly trusting an unverified email).
            if (googleUser.EmailVerified && googleUser.Email is not null)
                user = await _db.Users.FirstOrDefaultAsync(u => u.Email == googleUser.Email, ct);
        }

        if (user is null)
        {
            user = new User
            {
                GoogleId = googleUser.Sub,
                Username = displayName,
                AvatarUrl = googleUser.Picture,
                Email = googleUser.EmailVerified ? googleUser.Email : null,
                LastLoginAt = DateTimeOffset.UtcNow,
            };
            _db.Users.Add(user);
            _db.UserProfiles.Add(new UserProfile { UserId = user.Id, DisplayName = displayName });
            _db.UserSettings.Add(new UserSettings { UserId = user.Id });
        }
        else
        {
            user.GoogleId ??= googleUser.Sub;
            user.AvatarUrl ??= googleUser.Picture;
            if (googleUser.EmailVerified) user.Email ??= googleUser.Email;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            user.LastLoginAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        var tokens = await IssueTokenPairAsync(user, ct);
        return Redirect(BuildFrontendRedirect(_googleOptions.FrontendCallbackUrl, tokens));
    }

    // ---------- Steam (OpenID 2.0) ----------

    [HttpGet("steam/login")]
    public IActionResult SteamLogin()
    {
        if (!_steam.IsConfigured)
            return BadRequest("Steam login is not configured on this server (missing Steam Web API key).");

        return Redirect(_steam.BuildAuthorizeUrl(_steamOptions.ReturnUrl, _steamOptions.Realm));
    }

    [HttpGet("steam/callback")]
    public async Task<IActionResult> SteamCallback(CancellationToken ct)
    {
        var query = Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        var steamId = await _steam.VerifyAndGetSteamIdAsync(query, ct);
        if (steamId is null)
            return BadRequest("Steam login could not be verified.");

        var profile = await _steam.GetPlayerSummaryAsync(steamId, ct);
        var username = profile?.PersonaName ?? $"SteamUser{steamId[^6..]}";
        var avatarUrl = profile?.AvatarUrl;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId, ct);

        if (user is null)
        {
            user = new User
            {
                SteamId = steamId,
                Username = username,
                AvatarUrl = avatarUrl,
                LastLoginAt = DateTimeOffset.UtcNow,
            };
            _db.Users.Add(user);
            _db.UserProfiles.Add(new UserProfile { UserId = user.Id, DisplayName = username });
            _db.UserSettings.Add(new UserSettings { UserId = user.Id });
        }
        else
        {
            user.Username = username;
            user.AvatarUrl = avatarUrl ?? user.AvatarUrl;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            user.LastLoginAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        var tokens = await IssueTokenPairAsync(user, ct);
        return Redirect(BuildFrontendRedirect(_steamOptions.FrontendCallbackUrl, tokens));
    }

    // ---------- Shared ----------

    [HttpPost("refresh")]
    public async Task<ActionResult<TokenResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var hash = _jwt.HashRefreshToken(request.RefreshToken);
        var session = await _db.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);

        if (session is null || !session.IsActive)
            return Unauthorized("Refresh token is invalid or expired.");

        session.RevokedAt = DateTimeOffset.UtcNow;
        var tokens = await IssueTokenPairAsync(session.User, ct);

        return Ok(tokens);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var hash = _jwt.HashRefreshToken(request.RefreshToken);
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        if (session is not null)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    private static string BuildFrontendRedirect(string frontendCallbackUrl, TokenResponse tokens) =>
        $"{frontendCallbackUrl}" +
        $"#access_token={Uri.EscapeDataString(tokens.AccessToken)}" +
        $"&refresh_token={Uri.EscapeDataString(tokens.RefreshToken)}";

    private async Task<TokenResponse> IssueTokenPairAsync(User user, CancellationToken ct)
    {
        var accessToken = _jwt.CreateAccessToken(user);
        var (refreshToken, refreshHash) = _jwt.CreateRefreshToken();

        _db.Sessions.Add(new Session
        {
            UserId = user.Id,
            RefreshTokenHash = refreshHash,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
        });

        await _db.SaveChangesAsync(ct);

        return new TokenResponse(accessToken, refreshToken, DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes));
    }
}
