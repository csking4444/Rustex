using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rustex.Api.Dtos;
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
    private readonly IJwtTokenService _jwt;
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwtOptions;
    private readonly DiscordOAuthOptions _discordOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IDiscordOAuthService discord,
        IJwtTokenService jwt,
        AppDbContext db,
        IOptions<JwtOptions> jwtOptions,
        IOptions<DiscordOAuthOptions> discordOptions,
        ILogger<AuthController> logger)
    {
        _discord = discord;
        _jwt = jwt;
        _db = db;
        _jwtOptions = jwtOptions.Value;
        _discordOptions = discordOptions.Value;
        _logger = logger;
    }

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

        var user = await _db.Users.Include(u => u.Profile).Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.DiscordId == discordUser.Id, ct);

        if (user is null)
        {
            user = new User
            {
                DiscordId = discordUser.Id,
                DiscordUsername = discordUser.Username,
                DiscordAvatar = discordUser.Avatar,
                Email = discordUser.Email,
            };
            _db.Users.Add(user);
            _db.UserProfiles.Add(new UserProfile { UserId = user.Id, DisplayName = discordUser.Username });
            _db.UserSettings.Add(new UserSettings { UserId = user.Id });
        }
        else
        {
            user.DiscordUsername = discordUser.Username;
            user.DiscordAvatar = discordUser.Avatar;
            user.Email = discordUser.Email ?? user.Email;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            user.LastLoginAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        var tokens = await IssueTokenPairAsync(user, ct);

        var redirectUrl = $"{_discordOptions.FrontendCallbackUrl}" +
            $"#access_token={Uri.EscapeDataString(tokens.AccessToken)}" +
            $"&refresh_token={Uri.EscapeDataString(tokens.RefreshToken)}";

        return Redirect(redirectUrl);
    }

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
