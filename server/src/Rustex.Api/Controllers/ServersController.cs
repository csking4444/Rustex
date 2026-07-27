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
[Route("api/servers")]
[Authorize]
[RequiresSubscription]
public class ServersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ISubscriptionService _subscriptions;

    public ServersController(AppDbContext db, ISubscriptionService subscriptions)
    {
        _db = db;
        _subscriptions = subscriptions;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<ServerResponse>>> List(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var servers = await _db.RustServers
            .Where(s => s.OwnerUserId == userId)
            .OrderByDescending(s => s.IsFavorite).ThenBy(s => s.Name)
            .ToListAsync(ct);

        var snapshots = await GetLatestSnapshotsAsync(servers.Select(s => s.Id), ct);
        return servers.Select(s => ToResponse(s, snapshots.GetValueOrDefault(s.Id))).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServerResponse>> Get(Guid id, CancellationToken ct)
    {
        var server = await _db.RustServers.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == CurrentUserId, ct);
        if (server is null) return NotFound();

        var snapshots = await GetLatestSnapshotsAsync(new[] { server.Id }, ct);
        return ToResponse(server, snapshots.GetValueOrDefault(server.Id));
    }

    // Queried per-server (not GroupBy + First-per-group) deliberately: "latest row per group"
    // doesn't reliably translate to SQL across EF Core/Npgsql versions, and this list is small
    // (a user's own servers), so N simple, guaranteed-translatable queries beats one clever
    // query that risks throwing at runtime.
    private async Task<Dictionary<Guid, ServerStatusSnapshot>> GetLatestSnapshotsAsync(IEnumerable<Guid> serverIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, ServerStatusSnapshot>();

        foreach (var serverId in serverIds)
        {
            var snapshot = await _db.ServerStatusSnapshots
                .Where(s => s.ServerId == serverId)
                .OrderByDescending(s => s.RecordedAt)
                .FirstOrDefaultAsync(ct);

            if (snapshot is not null) result[serverId] = snapshot;
        }

        return result;
    }

    [HttpPost]
    public async Task<ActionResult<ServerResponse>> Create([FromBody] CreateServerRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;

        // The plan's server allowance is enforced here rather than only in the UI — hiding the
        // "add server" button does nothing to stop a direct POST.
        var entitlement = await _subscriptions.GetEntitlementAsync(userId, ct);
        var owned = await _db.RustServers.CountAsync(s => s.OwnerUserId == userId, ct);
        if (owned >= entitlement.ServerLimit)
        {
            return StatusCode(EntitlementStatus.UpgradeRequired, new
            {
                error = "server_limit_reached",
                message = $"Your {entitlement.PlanName} plan covers {entitlement.ServerLimit} server(s). Upgrade to add more.",
                limit = entitlement.ServerLimit,
                current = owned,
            });
        }

        var server = new RustServer
        {
            OwnerUserId = userId,
            Name = request.Name,
            IpAddress = request.IpAddress,
            GamePort = request.GamePort,
            QueryPort = request.QueryPort,
            MapName = request.MapName,
            Seed = request.Seed,
            WorldSize = request.WorldSize,
            Description = request.Description,
            Tags = request.Tags ?? new List<string>(),
            WipeSchedule = request.WipeSchedule,
            RestartSchedule = request.RestartSchedule,
        };

        _db.RustServers.Add(server);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = server.Id }, ToResponse(server, null));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ServerResponse>> Update(Guid id, [FromBody] UpdateServerRequest request, CancellationToken ct)
    {
        var server = await _db.RustServers.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == CurrentUserId, ct);
        if (server is null) return NotFound();

        server.Name = request.Name;
        server.IpAddress = request.IpAddress;
        server.GamePort = request.GamePort;
        server.QueryPort = request.QueryPort;
        server.MapName = request.MapName;
        server.Seed = request.Seed;
        server.WorldSize = request.WorldSize;
        server.Description = request.Description;
        server.Tags = request.Tags ?? server.Tags;
        server.IsFavorite = request.IsFavorite;
        server.WipeSchedule = request.WipeSchedule;
        server.RestartSchedule = request.RestartSchedule;
        server.AutoReconnect = request.AutoReconnect;
        server.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        var snapshots = await GetLatestSnapshotsAsync(new[] { server.Id }, ct);
        return ToResponse(server, snapshots.GetValueOrDefault(server.Id));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var server = await _db.RustServers.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == CurrentUserId, ct);
        if (server is null) return NotFound();

        _db.RustServers.Remove(server);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ServerResponse ToResponse(RustServer s, ServerStatusSnapshot? snapshot) => new(
        s.Id, s.Name, s.IpAddress, s.GamePort, s.QueryPort, s.MapName, s.Seed, s.WorldSize,
        s.Description, s.Status, s.Tags, s.IsFavorite, s.WipeSchedule, s.RestartSchedule,
        s.AutoReconnect, s.CreatedAt,
        snapshot?.PingMs, snapshot?.PlayerCount, snapshot?.MaxPlayers, snapshot?.QueueSize, snapshot?.RecordedAt);
}
