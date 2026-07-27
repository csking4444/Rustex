using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Auth;
using Rustex.Domain.Billing;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId:guid}/map")]
[Authorize]
[RequiresSubscription]
public class MapController : ControllerBase
{
    private readonly AppDbContext _db;

    public MapController(AppDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<MapResponse>> Get(Guid serverId, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        var map = await GetOrCreateMapAsync(serverId, ct);
        return ToResponse(map);
    }

    [HttpPut]
    public async Task<ActionResult<MapResponse>> Update(Guid serverId, [FromBody] UpdateMapRequest request, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        var map = await GetOrCreateMapAsync(serverId, ct);
        map.ImageUrl = request.ImageUrl;
        map.Width = request.Width;
        map.Height = request.Height;
        map.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ToResponse(map);
    }

    [HttpGet("markers")]
    public async Task<ActionResult<List<MarkerResponse>>> ListMarkers(Guid serverId, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        var map = await GetOrCreateMapAsync(serverId, ct);
        var markers = await _db.Markers.Where(m => m.MapId == map.Id).ToListAsync(ct);
        return markers.Select(ToResponse).ToList();
    }

    [HttpPost("markers")]
    public async Task<ActionResult<MarkerResponse>> CreateMarker(Guid serverId, [FromBody] CreateMarkerRequest request, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        var map = await GetOrCreateMapAsync(serverId, ct);
        var marker = new Marker
        {
            MapId = map.Id,
            CreatedBy = CurrentUserId,
            Type = request.Type,
            X = request.X,
            Y = request.Y,
            Label = request.Label,
            Color = request.Color,
            IsShared = request.IsShared,
        };

        _db.Markers.Add(marker);
        await _db.SaveChangesAsync(ct);
        return ToResponse(marker);
    }

    [HttpPut("markers/{id:guid}")]
    public async Task<ActionResult<MarkerResponse>> UpdateMarker(Guid serverId, Guid id, [FromBody] UpdateMarkerRequest request, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        var map = await GetOrCreateMapAsync(serverId, ct);
        var marker = await _db.Markers.FirstOrDefaultAsync(m => m.Id == id && m.MapId == map.Id, ct);
        if (marker is null) return NotFound();

        marker.Label = request.Label;
        marker.Color = request.Color;
        marker.IsShared = request.IsShared;

        await _db.SaveChangesAsync(ct);
        return ToResponse(marker);
    }

    [HttpDelete("markers/{id:guid}")]
    public async Task<IActionResult> DeleteMarker(Guid serverId, Guid id, CancellationToken ct)
    {
        if (!await OwnsServerAsync(serverId, ct)) return NotFound();

        var map = await GetOrCreateMapAsync(serverId, ct);
        var marker = await _db.Markers.FirstOrDefaultAsync(m => m.Id == id && m.MapId == map.Id, ct);
        if (marker is null) return NotFound();

        _db.Markers.Remove(marker);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> OwnsServerAsync(Guid serverId, CancellationToken ct) =>
        _db.RustServers.AnyAsync(s => s.Id == serverId && s.OwnerUserId == CurrentUserId, ct);

    private async Task<MapData> GetOrCreateMapAsync(Guid serverId, CancellationToken ct)
    {
        var map = await _db.Maps.FirstOrDefaultAsync(m => m.ServerId == serverId, ct);
        if (map is not null) return map;

        map = new MapData { ServerId = serverId };
        _db.Maps.Add(map);
        await _db.SaveChangesAsync(ct);
        return map;
    }

    private static MapResponse ToResponse(MapData m) => new(m.Id, m.ServerId, m.ImageUrl, m.Width, m.Height, m.UpdatedAt);

    private static MarkerResponse ToResponse(Marker m) => new(
        m.Id, m.MapId, m.CreatedBy, m.Type, m.X, m.Y, m.Label, m.Color, m.IsShared, m.CreatedAt);
}
