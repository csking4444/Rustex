using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
    private readonly ISteamOpenIdStateStore _steamState;
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
        ISteamOpenIdStateStore steamState,
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
        _steamState = steamState;
        _passwordAuth = passwordAuth;
        _jwt = jwt;
        _db = db;
        _jwtOptions = jwtOptions.Value;
        _discordOptions = discordOptions.Value;
        _googleOptions = googleOptions.Value;
        _steamOptions = steamOptions.Value;
        _logger = logger;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // ---------- Email + password ----------

    [HttpPost("register")]
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    //
    // OpenID 2.0 has no `state` param, so CSRF protection and login-vs-link intent both ride
    // inside a single-use nonce embedded in `openid.return_to` (see SteamOpenIdStateStore) — it's
    // inside Steam's signed field set, so it can't be tampered with in transit. The rw_oauth_state
    // cookie used by Discord/Google is layered on top as a secondary check when present, but isn't
    // required, since Steam's redirect chain is the primary defence here.

    /// <param name="force">Send the user to Steam's sign-in form even if their browser already has
    /// a Steam session. Without it Steam auto-approves and bounces straight back — correct SSO, but
    /// it makes the button look like it did nothing, and gives no way to pick a different account.</param>
    [HttpGet("steam/login")]
    [AllowAnonymous]
    public async Task<IActionResult> SteamLogin([FromQuery] bool force = false)
    {
        if (!_steam.IsConfigured)
            return BadRequest("Steam login is not enabled on this server.");

        var nonce = await _steamState.IssueAsync(SteamAuthPurpose.Login);
        SetStateCookie(nonce);
        return Redirect(_steam.BuildAuthorizeUrl(_steamOptions.ReturnUrl, _steamOptions.Realm, nonce, force));
    }

    /// <summary>Attaches Steam to the caller's already-signed-in account. A top-level browser
    /// redirect can't carry a bearer header, so which account this is for has to be pre-encoded
    /// in the nonce rather than read off the request when Steam redirects back.</summary>
    [HttpPost("steam/link/start")]
    [Authorize]
    public async Task<ActionResult<SteamLinkStartResponse>> SteamLinkStart([FromQuery] bool force = false)
    {
        if (!_steam.IsConfigured)
            return BadRequest("Steam login is not enabled on this server.");

        var nonce = await _steamState.IssueAsync(SteamAuthPurpose.Link, CurrentUserId);
        SetStateCookie(nonce);
        return new SteamLinkStartResponse(_steam.BuildAuthorizeUrl(_steamOptions.ReturnUrl, _steamOptions.Realm, nonce, force));
    }

    [HttpDelete("steam/link")]
    [Authorize]
    public async Task<IActionResult> SteamUnlink(CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId, ct);
        if (user is null) return NotFound();

        if (user.SteamId is null) return NoContent();

        if (user.PasswordHash is null && user.DiscordId is null && user.GoogleId is null)
            return BadRequest("Steam is your only way to sign in — link another method before unlinking it.");

        user.SteamId = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("steam/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> SteamCallback(CancellationToken ct)
    {
        var query = Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        var verification = await _steam.VerifyAsync(query, ct);
        if (verification is null)
            return BadRequest("Steam login could not be verified.");

        var state = await _steamState.ConsumeAsync(verification.ReturnToState);
        if (state is null)
            return BadRequest("This Steam login link has expired or was already used — try signing in again.");

        // Secondary check: if the cookie made it back, it must agree with the nonce Steam signed.
        // Its absence isn't fatal (some browsers drop it across the cross-site redirect), since
        // the Redis-backed single-use nonce above is what's actually load-bearing here.
        if (Request.Cookies.TryGetValue(StateCookieName, out var cookieState) && cookieState != verification.ReturnToState)
            return BadRequest("Invalid OAuth state.");
        Response.Cookies.Delete(StateCookieName);

        if (!await _steamState.MarkNonceUsedOnceAsync(verification.ResponseNonce))
            return BadRequest("This Steam login response was already used.");

        var steamId = verification.SteamId64;

        if (state.Purpose == SteamAuthPurpose.Link)
        {
            var linkUserId = state.LinkUserId!.Value;
            var owner = await _db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId, ct);
            if (owner is not null && owner.Id != linkUserId)
                return Redirect(BuildFrontendErrorRedirect(_steamOptions.FrontendCallbackUrl, "steam_already_linked"));

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == linkUserId, ct);
            if (user is null) return NotFound();

            user.SteamId = steamId;
            var profile = await _steam.GetPlayerSummaryAsync(steamId, ct);
            user.AvatarUrl ??= profile?.AvatarUrl;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Race backstop: two link attempts for the same SteamId landed concurrently.
                return Redirect(BuildFrontendErrorRedirect(_steamOptions.FrontendCallbackUrl, "steam_already_linked"));
            }

            // Already signed in — no tokens to hand back, just land on settings.
            return Redirect($"{_steamOptions.FrontendCallbackUrl.Replace("/auth/callback", "/settings")}?linked=steam");
        }

        // Purpose.Login — deliberately no auto-link by email or anything else: Steam gives us no
        // verifiable email to link on, unlike Google. An email-registered user who clicks "Sign in
        // with Steam" gets a second account; linking Steam to an existing account is a separate,
        // explicit action via steam/link/start.
        var loginUser = await _db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId, ct);
        var loginProfile = await _steam.GetPlayerSummaryAsync(steamId, ct);
        var username = loginProfile?.PersonaName ?? $"SteamUser{steamId[^6..]}";
        var avatarUrl = loginProfile?.AvatarUrl;

        if (loginUser is null)
        {
            loginUser = new User
            {
                SteamId = steamId,
                Username = username,
                AvatarUrl = avatarUrl,
                LastLoginAt = DateTimeOffset.UtcNow,
            };
            _db.Users.Add(loginUser);
            _db.UserProfiles.Add(new UserProfile { UserId = loginUser.Id, DisplayName = username });
            _db.UserSettings.Add(new UserSettings { UserId = loginUser.Id });
        }
        else
        {
            loginUser.Username = username;
            loginUser.AvatarUrl = avatarUrl ?? loginUser.AvatarUrl;
            loginUser.UpdatedAt = DateTimeOffset.UtcNow;
            loginUser.LastLoginAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);


        var tokens = await IssueTokenPairAsync(loginUser, ct);
        return Redirect(BuildFrontendRedirect(_steamOptions.FrontendCallbackUrl, tokens));
    }

    private void SetStateCookie(string state) =>
        Response.Cookies.Append(StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10),
        });

    private static string BuildFrontendErrorRedirect(string frontendCallbackUrl, string error) =>
        $"{frontendCallbackUrl}?error={Uri.EscapeDataString(error)}";

    // ---------- Shared ----------

    [HttpPost("refresh")]
    [AllowAnonymous]
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
    [AllowAnonymous]
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
