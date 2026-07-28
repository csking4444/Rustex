using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Auth;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId:guid}/raid-alarm-settings")]
[Authorize]
public class RaidAlarmSettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public RaidAlarmSettingsController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<RaidAlarmSettingsResponse>> Get(Guid serverId, CancellationToken ct)
    {
        var owns = await _db.RustServers.AnyAsync(s => s.Id == serverId && s.OwnerUserId == CurrentUserId, ct);
        if (!owns) return NotFound();

        var settings = await _db.RaidAlarmSettings.FirstOrDefaultAsync(s => s.ServerId == serverId, ct);
        return ToResponse(settings ?? new RaidAlarmSettings { ServerId = serverId });
    }

    [HttpPut]
    public async Task<ActionResult<RaidAlarmSettingsResponse>> Update(
        Guid serverId, [FromBody] UpdateRaidAlarmSettingsRequest request, CancellationToken ct)
    {
        var owns = await _db.RustServers.AnyAsync(s => s.Id == serverId && s.OwnerUserId == CurrentUserId, ct);
        if (!owns) return NotFound();

        if (request.Tier1Threshold < 1 || request.Tier2Threshold < request.Tier1Threshold || request.Tier3Threshold < request.Tier2Threshold)
            return BadRequest("Thresholds must be positive and non-decreasing: Tier 1 <= Tier 2 <= Tier 3.");

        if (request.TimeWindowSeconds < 1 || request.CooldownSeconds < 0 || request.ClusterRadius < 0)
            return BadRequest("TimeWindowSeconds must be positive; CooldownSeconds/ClusterRadius must not be negative.");

        var settings = await _db.RaidAlarmSettings.FirstOrDefaultAsync(s => s.ServerId == serverId, ct);
        if (settings is null)
        {
            settings = new RaidAlarmSettings { ServerId = serverId };
            _db.RaidAlarmSettings.Add(settings);
        }

        settings.IsEnabled = request.IsEnabled;
        settings.Tier1Threshold = request.Tier1Threshold;
        settings.Tier2Threshold = request.Tier2Threshold;
        settings.Tier3Threshold = request.Tier3Threshold;
        settings.TimeWindowSeconds = request.TimeWindowSeconds;
        settings.ClusterRadius = request.ClusterRadius;
        settings.CooldownSeconds = request.CooldownSeconds;
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ToResponse(settings);
    }

    private static RaidAlarmSettingsResponse ToResponse(RaidAlarmSettings s) => new(
        s.ServerId, s.IsEnabled, s.Tier1Threshold, s.Tier2Threshold, s.Tier3Threshold,
        s.TimeWindowSeconds, s.ClusterRadius, s.CooldownSeconds, s.UpdatedAt);
}
