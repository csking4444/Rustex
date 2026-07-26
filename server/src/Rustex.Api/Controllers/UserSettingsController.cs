using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/users/me/settings")]
[Authorize]
public class UserSettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public UserSettingsController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<UserSettingsResponse>> Get(CancellationToken ct)
    {
        var settings = await GetOrCreateAsync(ct);
        return ToResponse(settings);
    }

    [HttpPut]
    public async Task<ActionResult<UserSettingsResponse>> Update([FromBody] UpdateUserSettingsRequest request, CancellationToken ct)
    {
        var hasStart = !string.IsNullOrWhiteSpace(request.QuietHoursStart);
        var hasEnd = !string.IsNullOrWhiteSpace(request.QuietHoursEnd);
        if (hasStart != hasEnd)
            return BadRequest("Quiet hours start and end must both be set, or both be empty.");

        TimeOnly? start = null;
        TimeOnly? end = null;
        if (hasStart)
        {
            if (!TryParseTime(request.QuietHoursStart!, out var parsedStart) || !TryParseTime(request.QuietHoursEnd!, out var parsedEnd))
                return BadRequest("Quiet hours must be in HH:mm format.");
            start = parsedStart;
            end = parsedEnd;
        }

        var settings = await GetOrCreateAsync(ct);

        settings.SoundEnabled = request.SoundEnabled;
        settings.DesktopEnabled = request.DesktopEnabled;
        settings.BrowserEnabled = request.BrowserEnabled;
        settings.DiscordEnabled = request.DiscordEnabled;
        settings.PushEnabled = request.PushEnabled;
        settings.CallEnabled = request.CallEnabled;
        settings.QuietHoursStart = start;
        settings.QuietHoursEnd = end;
        settings.QuietHoursTimezone = string.IsNullOrWhiteSpace(request.QuietHoursTimezone) ? "UTC" : request.QuietHoursTimezone;
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ToResponse(settings);
    }

    private static bool TryParseTime(string value, out TimeOnly result) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    private async Task<UserSettings> GetOrCreateAsync(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings is not null) return settings;

        // AuthController creates this row for every new user, but fall back defensively —
        // e.g. for users who existed before this table did.
        settings = new UserSettings { UserId = userId };
        _db.UserSettings.Add(settings);
        await _db.SaveChangesAsync(ct);
        return settings;
    }

    private static UserSettingsResponse ToResponse(UserSettings s) => new(
        s.SoundEnabled, s.DesktopEnabled, s.BrowserEnabled, s.DiscordEnabled, s.PushEnabled, s.CallEnabled,
        s.QuietHoursStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
        s.QuietHoursEnd?.ToString("HH:mm", CultureInfo.InvariantCulture),
        s.QuietHoursTimezone, s.UpdatedAt);
}
