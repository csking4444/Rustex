using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Auth;
using Rustex.Api.Dtos;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/raid-events")]
[Authorize]
public class RaidEventsController : ControllerBase
{
    private readonly AppDbContext _db;

    public RaidEventsController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet("recent")]
    public async Task<ActionResult<List<RaidEventResponse>>> Recent([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        limit = Math.Clamp(limit, 1, 100);

        var events = await _db.RaidEvents
            .Include(r => r.Server)
            .Where(r => r.Server.OwnerUserId == userId)
            .OrderByDescending(r => r.DetectedAt)
            .Take(limit)
            .ToListAsync(ct);

        return events.Select(r => new RaidEventResponse(
            r.Id, r.ServerId, r.Server.Name, r.DetectedAt, r.Grid, r.Tier,
            r.RaidType, r.ExplosionCount, r.EstimatedSize, r.Status)).ToList();
    }
}
