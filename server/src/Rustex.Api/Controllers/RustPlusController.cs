using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus;
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
    private const int VendingMachineMarkerType = 3;

    private readonly AppDbContext _db;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly IEncryptionService? _encryption;

    public RustPlusController(
        AppDbContext db,
        RustPlusConnectionManager connectionManager,
        IEncryptionService? encryption = null)
    {
        _db = db;
        _connectionManager = connectionManager;
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
        pairing.PlayerTokenEncrypted = _encryption.Encrypt(request.PlayerToken);
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
                .Where(m => m.Type == VendingMachineMarkerType)
                .Select(m => new RustPlusVendingMachineResponse(
                    m.Id, m.X, m.Y,
                    m.SellOrders.Select(o => new RustPlusSellOrderResponse(o.ItemId, o.Quantity, o.CurrencyId, o.CostPerItem, o.AmountInStock)).ToList()))
                .ToList();
            return vendingMachines;
        }
        catch (Exception ex)
        {
            return StatusCode(502, $"Rust+ request failed: {ex.Message}");
        }
    }

    private async Task<(RustPlusClient? Client, ActionResult? Error)> ConnectAsync(Guid serverId, CancellationToken ct)
    {
        if (_encryption is null)
            return (null, StatusCode(503, "This server has no Encryption:FieldKey configured."));

        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (pairing is null)
            return (null, NotFound("No Rust+ pairing saved for this server yet."));

        try
        {
            var token = uint.Parse(_encryption.Decrypt(pairing.PlayerTokenEncrypted));
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
