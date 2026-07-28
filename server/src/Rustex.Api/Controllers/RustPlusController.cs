using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Auth;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Domain.RustPlus;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus;
using Rustex.Infrastructure.RustPlus.Proto;
using Rustex.Infrastructure.Security;

namespace Rustex.Api.Controllers;

/// <summary>
/// Manual Rust+ pairing — the user supplies a (playerId, playerToken) obtained some other way (a
/// community pairing tool, or the Rustex "Log In &amp; Pair" flow once RustPlusAccountController
/// exists). Once paired, this exposes GetTeamInfo and vending-machine map markers live from the
/// game server. See docs/RUSTPLUS.md for what's verified vs. best-effort in this subsystem.
/// </summary>
[ApiController]
[Route("api/servers/{serverId:guid}/rustplus")]
[Authorize]
public class RustPlusController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly IRustItemCatalog _catalog;
    private readonly IEncryptionService? _encryption;

    public RustPlusController(
        AppDbContext db,
        RustPlusConnectionManager connectionManager,
        IRustItemCatalog catalog,
        IEncryptionService? encryption = null)
    {
        _db = db;
        _connectionManager = connectionManager;
        _catalog = catalog;
        _encryption = encryption;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet("pairing")]
    public async Task<ActionResult<RustPlusPairingResponse>> GetPairing(Guid serverId, CancellationToken ct)
    {
        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (pairing is null) return NotFound();
        return ToResponse(pairing);
    }

    [HttpPost("pairing")]
    public async Task<ActionResult<RustPlusPairingResponse>> SavePairing(Guid serverId, [FromBody] CreateRustPlusPairingRequest request, CancellationToken ct)
    {
        if (_encryption is null)
            return StatusCode(503, "This server has no Encryption:FieldKey configured, so Rust+ tokens can't be stored securely.");

        if (!RustPlusTokenFormat.TryNormalize(request.PlayerToken, out var signedToken))
            return BadRequest("playerToken must fit in 32 bits — check you copied the whole value from your pairing tool.");

        var ownsServer = await _db.RustServers.AnyAsync(s => s.Id == serverId && s.OwnerUserId == CurrentUserId, ct);
        if (!ownsServer) return NotFound();

        var userId = CurrentUserId;
        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.ServerId == serverId && p.UserId == userId, ct);

        if (pairing is null)
        {
            pairing = new RustPlusPairing { UserId = userId, ServerId = serverId };
            _db.RustPlusPairings.Add(pairing);
        }
        else
        {
            await _connectionManager.DropAsync(pairing.Id); // force a reconnect with the new credentials
        }

        pairing.PlayerId = request.PlayerId;
        pairing.PlayerTokenEncrypted = _encryption.Encrypt(signedToken.ToString());
        pairing.ServerIp = request.ServerIp;
        pairing.ServerPort = request.ServerPort;

        await _db.SaveChangesAsync(ct);
        return ToResponse(pairing);
    }

    /// <summary>
    /// Superseded — the old per-request Steam-auth-ticket auto-pair flow (which blocked an HTTP
    /// request for up to two minutes) has been replaced by a one-time local setup: run
    /// `rustex-pair` once, then pair any server from the in-game Rust+ pause-menu tab. See
    /// docs/RUSTPLUS.md and POST /api/rustplus/link-codes.
    /// </summary>
    [HttpPost("auto-pair")]
    [Obsolete("Replaced by the rustex-pair local helper + RustPlusAccountController.")]
    public IActionResult AutoPair(Guid serverId) =>
        StatusCode(410, "Auto-pairing moved — see /settings/rust-plus to generate a one-time setup code, then run `rustex-pair`. Manual pairing (POST pairing) still works as before.");

    [HttpDelete("pairing")]
    public async Task<IActionResult> DeletePairing(Guid serverId, CancellationToken ct)
    {
        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (pairing is null) return NotFound();

        await _connectionManager.DropAsync(pairing.Id);
        _db.RustPlusPairings.Remove(pairing);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("team")]
    public async Task<ActionResult<RustPlusTeamInfoResponse>> GetTeamInfo(Guid serverId, CancellationToken ct)
    {
        var clientResult = await ConnectAsync(serverId, ct);
        if (clientResult.Error is not null) return clientResult.Error;

        try
        {
            var teamInfo = await clientResult.Client!.GetTeamInfoAsync(ct);
            var members = teamInfo.Members
                .Select(m => new RustPlusTeamMemberResponse(m.SteamId, m.Name, m.X, m.Y, m.IsOnline, m.IsAlive))
                .ToList();
            return new RustPlusTeamInfoResponse(teamInfo.LeaderSteamId, members);
        }
        catch (Exception ex)
        {
            return StatusCode(502, $"Rust+ request failed: {ex.Message}");
        }
    }

    [HttpGet("vending-machines")]
    public async Task<ActionResult<List<RustPlusVendingMachineResponse>>> GetVendingMachines(Guid serverId, CancellationToken ct)
    {
        var clientResult = await ConnectAsync(serverId, ct);
        if (clientResult.Error is not null) return clientResult.Error;

        try
        {
            var markers = await clientResult.Client!.GetMapMarkersAsync(ct);
            var vendingMachines = markers
                .Where(m => m.Type == AppMarkerType.VendingMachine)
                .Select(m => new RustPlusVendingMachineResponse(
                    (int)m.Id, m.X, m.Y,
                    m.SellOrders.Select(o => new RustPlusSellOrderResponse(o.ItemId, o.Quantity, o.CurrencyId, o.CostPerItem, o.AmountInStock)).ToList()))
                .ToList();
            return vendingMachines;
        }
        catch (Exception ex)
        {
            return StatusCode(502, $"Rust+ request failed: {ex.Message}");
        }
    }

    /// <summary>
    /// DB-backed team roster, kept fresh by RustPlusTeamTrackingWorker (teamChanged broadcasts +
    /// a 30s fallback poll) — unlike GET team, this never blocks on a live socket round-trip.
    /// </summary>
    [HttpGet("team-state")]
    public async Task<ActionResult<List<RustPlusTeamMemberStateResponse>>> GetTeamState(Guid serverId, CancellationToken ct)
    {
        var hasPairing = await _db.RustPlusPairings.AnyAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (!hasPairing) return NotFound("No Rust+ pairing saved for this server yet.");

        var members = await _db.RustPlusTeamMemberStates
            .Where(s => s.ServerId == serverId)
            .OrderByDescending(s => s.IsOnline).ThenBy(s => s.Name)
            .Select(s => new RustPlusTeamMemberStateResponse(s.SteamId, s.Name, s.IsOnline, s.IsAlive, s.LastX, s.LastY, s.LastGrid, s.LastSeenAt, s.UpdatedAt))
            .ToListAsync(ct);
        return members;
    }

    /// <summary>
    /// DB-backed vending search, populated by RustPlusVendingPollWorker — search reads the
    /// database, so typing on every keystroke never round-trips to the game server.
    /// </summary>
    [HttpGet("vending/search")]
    public async Task<ActionResult<List<RustPlusVendingSearchResultResponse>>> SearchVending(
        Guid serverId, [FromQuery] string? q, [FromQuery] int? maxCost, [FromQuery] bool inStockOnly = false, CancellationToken ct = default)
    {
        var hasPairing = await _db.RustPlusPairings.AnyAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (!hasPairing) return NotFound("No Rust+ pairing saved for this server yet.");

        var listings = await _db.VendingListings
            .Include(l => l.Snapshot)
            .Where(l => l.Snapshot.ServerId == serverId)
            .ToListAsync(ct);

        var results = new List<RustPlusVendingSearchResultResponse>();
        foreach (var listing in listings)
        {
            if (maxCost is not null && listing.CostPerItem > maxCost) continue;
            if (inStockOnly && listing.AmountInStock <= 0) continue;

            var item = _catalog.Find(listing.ItemId);
            var itemName = item?.Name ?? $"Item {listing.ItemId}";
            if (!string.IsNullOrWhiteSpace(q) &&
                !itemName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !(item?.Shortname.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)) continue;

            var currency = _catalog.Find(listing.CurrencyId);
            results.Add(new RustPlusVendingSearchResultResponse(
                listing.Snapshot.MarkerId, listing.Snapshot.Name, listing.Snapshot.Grid,
                listing.ItemId, itemName, listing.CostPerItem, listing.CurrencyId,
                currency?.Name ?? (listing.CurrencyIsBlueprint ? "Scrap" : $"Item {listing.CurrencyId}"),
                listing.CurrencyIsBlueprint, listing.AmountInStock, listing.UpdatedAt));
        }

        return results.OrderBy(r => r.CostPerItem).Take(200).ToList();
    }

    /// <summary>Recent team chat, ingested by RustPlusChatAssistantWorker from the teamMessage
    /// broadcast — includes the assistant's own replies (IsFromAssistant) so the web UI can show
    /// one unified feed.</summary>
    [HttpGet("chat")]
    public async Task<ActionResult<List<RustPlusChatMessageResponse>>> GetChat(Guid serverId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var hasPairing = await _db.RustPlusPairings.AnyAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (!hasPairing) return NotFound("No Rust+ pairing saved for this server yet.");

        var messages = await _db.RustPlusChatMessages
            .Where(m => m.ServerId == serverId)
            .OrderByDescending(m => m.SentAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(m => new RustPlusChatMessageResponse(m.SteamId, m.Name, m.Message, m.IsFromAssistant, m.SentAt))
            .ToListAsync(ct);
        messages.Reverse();
        return messages;
    }

    /// <summary>Sends a message into the game's team chat from the web dashboard, using the same
    /// live connection Team Tracking/Chat Assistant already keep warm.</summary>
    [HttpPost("chat")]
    public async Task<IActionResult> SendChat(Guid serverId, [FromBody] SendRustPlusChatMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) return BadRequest("Message can't be empty.");

        var clientResult = await ConnectAsync(serverId, ct);
        if (clientResult.Error is not null) return clientResult.Error;

        try
        {
            await clientResult.Client!.SendTeamMessageAsync(request.Message, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(502, $"Rust+ request failed: {ex.Message}");
        }

        // RustPlusChatAssistantWorker's broadcast handler skips messages from our own paired
        // identity (to avoid double-recording its own auto-replies), so this endpoint has to
        // record the message it just sent itself.
        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        _db.RustPlusChatMessages.Add(new RustPlusChatMessage
        {
            ServerId = serverId,
            SteamId = pairing?.PlayerId ?? 0,
            Name = "Rustex",
            Message = request.Message,
            IsFromAssistant = true,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<(RustPlusClient? Client, ActionResult? Error)> ConnectAsync(Guid serverId, CancellationToken ct)
    {
        if (_encryption is null)
            return (null, StatusCode(503, "This server has no Encryption:FieldKey configured."));

        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (pairing is null)
            return (null, NotFound("No Rust+ pairing saved for this server yet."));

        if (!int.TryParse(_encryption.Decrypt(pairing.PlayerTokenEncrypted), out var token))
            return (null, Conflict("The saved Rust+ token for this pairing is malformed — delete and re-pair this server."));

        try
        {
            var client = await _connectionManager.GetOrConnectAsync(pairing, token, ct);

            pairing.LastConnectedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            return (client, null);
        }
        catch (Exception ex)
        {
            return (null, StatusCode(502, $"Couldn't connect to the Rust+ server: {ex.Message}"));
        }
    }

    private static RustPlusPairingResponse ToResponse(RustPlusPairing p) =>
        new(p.Id, p.ServerId, p.PlayerId, p.ServerIp, p.ServerPort, p.CreatedAt, p.LastConnectedAt);
}
