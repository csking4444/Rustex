using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Auth;
using Rustex.Api.Dtos;
using Rustex.Domain;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

/// <summary>
/// On-demand aggregation over RaidEvents/ServerStatusSnapshots, computed at request time rather
/// than via a precomputed AnalyticsSnapshot background job — always fresh, and simple aggregates
/// (Count/Average) translate reliably to SQL, unlike "first row per group" patterns. A
/// snapshot-based rollup is worth adding once raid volume is large enough that scanning the raw
/// tables per request stops being cheap (see docs/ROADMAP.md Phase 9).
/// </summary>
[ApiController]
[Route("api/servers/{serverId:guid}/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnalyticsController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet("summary")]
    public async Task<ActionResult<AnalyticsSummaryResponse>> Summary(Guid serverId, [FromQuery] int days = 7, CancellationToken ct = default)
    {
        var owns = await _db.RustServers.AnyAsync(s => s.Id == serverId && s.OwnerUserId == CurrentUserId, ct);
        if (!owns) return NotFound();

        days = Math.Clamp(days, 1, 90);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        var raidEvents = await _db.RaidEvents
            .Where(r => r.ServerId == serverId && r.DetectedAt >= cutoff)
            .Select(r => new { r.DetectedAt, r.Tier })
            .ToListAsync(ct);

        var byDay = raidEvents
            .GroupBy(r => DateOnly.FromDateTime(r.DetectedAt.UtcDateTime))
            .Select(g => new DailyRaidCount(g.Key, g.Count()))
            .OrderBy(d => d.Date)
            .ToList();

        var byHour = Enumerable.Range(0, 24)
            .Select(hour => new HourlyRaidCount(hour, raidEvents.Count(r => r.DetectedAt.UtcDateTime.Hour == hour)))
            .ToList();

        var snapshots = _db.ServerStatusSnapshots.Where(s => s.ServerId == serverId && s.RecordedAt >= cutoff);
        var avgPing = await snapshots.Select(s => (double?)s.PingMs).AverageAsync(ct);
        var avgPlayers = await snapshots.Select(s => (double?)s.PlayerCount).AverageAsync(ct);
        var peakPlayers = await snapshots.Select(s => s.PlayerCount).MaxAsync(ct);

        return new AnalyticsSummaryResponse(
            serverId,
            days,
            raidEvents.Count,
            raidEvents.Count(r => r.Tier == RaidTier.Tier1),
            raidEvents.Count(r => r.Tier == RaidTier.Tier2),
            raidEvents.Count(r => r.Tier == RaidTier.Tier3),
            byDay,
            byHour,
            avgPing,
            avgPlayers,
            peakPlayers);
    }
}
