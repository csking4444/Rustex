using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RustPlusApi.Fcm.Data;
using Rustex.Api.Auth;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Auth;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus.Fcm;

namespace Rustex.Api.Controllers;

/// <summary>
/// The "log in and pair" flow: a signed-in user generates a one-time code here, types it into
/// the <c>rustex-pair</c> local helper running on their own machine, and the helper exchanges it
/// for a narrowly-scoped token that can only upload Rust+ push credentials — nothing that reads
/// user data or mints a session. See docs/RUSTPLUS.md.
/// </summary>
[ApiController]
[Route("api/rustplus")]
public class RustPlusAccountController : ControllerBase
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // excludes I,L,O,U — no ambiguous chars
    private static readonly TimeSpan ScopedTokenTtl = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IRustPlusCredentialStore _credentialStore;
    private readonly RustPlusOptions _options;

    public RustPlusAccountController(
        AppDbContext db,
        IJwtTokenService jwt,
        IRustPlusCredentialStore credentialStore,
        IOptions<RustPlusOptions> options)
    {
        _db = db;
        _jwt = jwt;
        _credentialStore = credentialStore;
        _options = options.Value;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpPost("link-codes")]
    [Authorize]
    public async Task<ActionResult<CreateLinkCodeResponse>> CreateLinkCode(CancellationToken ct)
    {
        var userId = CurrentUserId;

        // Only one live code per user at a time — generating a new one retires any earlier
        // unconsumed code rather than leaving multiple valid codes floating around.
        var previous = await _db.RustPlusLinkCodes
            .Where(c => c.UserId == userId && c.ConsumedAt == null && c.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(ct);
        foreach (var old in previous) old.ConsumedAt = DateTimeOffset.UtcNow;

        var code = GenerateCode();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.PairingCodeTtlMinutes);

        _db.RustPlusLinkCodes.Add(new RustPlusLinkCode
        {
            UserId = userId,
            CodeHash = Hash(code),
            ExpiresAt = expiresAt,
            CreatedFromIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await _db.SaveChangesAsync(ct);

        return new CreateLinkCodeResponse(FormatForDisplay(code), expiresAt);
    }

    [HttpPost("link-codes/redeem")]
    [AllowAnonymous]
    public async Task<ActionResult<RedeemLinkCodeResponse>> RedeemLinkCode([FromBody] RedeemLinkCodeRequest request, CancellationToken ct)
    {
        var normalized = request.Code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        var hash = Hash(normalized);

        var linkCode = await _db.RustPlusLinkCodes.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.CodeHash == hash, ct);

        if (linkCode is null || linkCode.ConsumedAt is not null || linkCode.ExpiresAt < DateTimeOffset.UtcNow)
            return BadRequest("That code is invalid, already used, or expired — generate a new one.");

        linkCode.ConsumedAt = DateTimeOffset.UtcNow;

        _db.Notifications.Add(new Notification
        {
            UserId = linkCode.UserId,
            Type = "rustplus.setup_code_used",
            Title = "Rust+ setup code used",
            Body = $"Your Rust+ setup code was redeemed from {HttpContext.Connection.RemoteIpAddress}. If this wasn't you, delete your Rust+ credentials in Settings.",
        });

        await _db.SaveChangesAsync(ct);

        var token = _jwt.CreateScopedToken(
            linkCode.UserId,
            RustPlusPairingAuthConstants.Audience,
            RustPlusPairingAuthConstants.CredentialWriteScope,
            ScopedTokenTtl);

        return new RedeemLinkCodeResponse(token, (int)ScopedTokenTtl.TotalSeconds);
    }

    [HttpPut("credentials")]
    [Authorize(Policy = RustPlusPairingAuthConstants.CredentialWritePolicy)]
    public async Task<IActionResult> UploadCredentials([FromBody] UploadCredentialsRequest request, CancellationToken ct)
    {
        var credentials = new Credentials
        {
            Gcm = new Gcm { AndroidId = request.Gcm.AndroidId, SecurityToken = request.Gcm.SecurityToken },
            Fcm = new FcmToken { Token = request.FcmToken },
            ExpoPushToken = request.ExpoPushToken,
        };

        await _credentialStore.SaveAsync(CurrentUserId, credentials, request.SteamId, ct);
        return NoContent();
    }

    [HttpGet("credentials/status")]
    [Authorize]
    public async Task<ActionResult<RustPlusCredentialStatusResponse>> GetCredentialStatus(CancellationToken ct)
    {
        var row = await _credentialStore.GetAsync(CurrentUserId, ct);
        if (row is null) return new RustPlusCredentialStatusResponse(false, null, null, null, null, null);

        return new RustPlusCredentialStatusResponse(
            true,
            row.Status.ToString(),
            row.RegisteredAt,
            row.RegisteredAt.AddDays(_options.CredentialLifetimeDays),
            row.LastNotificationAt,
            row.SteamId);
    }

    [HttpDelete("credentials")]
    [Authorize]
    public async Task<IActionResult> DeleteCredentials(CancellationToken ct)
    {
        await _credentialStore.DeleteAsync(CurrentUserId, ct);
        return NoContent();
    }

    private static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var sb = new StringBuilder(8);
        foreach (var b in bytes) sb.Append(CrockfordAlphabet[b % CrockfordAlphabet.Length]);
        return sb.ToString();
    }

    private static string FormatForDisplay(string code) => $"RSTX-{code[..4]}-{code[4..]}";

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
