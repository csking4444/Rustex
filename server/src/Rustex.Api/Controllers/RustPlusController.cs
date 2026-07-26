using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus;
using Rustex.Infrastructure.RustPlus.Fcm;
using Rustex.Infrastructure.Security;

namespace Rustex.Api.Controllers;

/// <summary>
/// Manual Rust+ pairing (Stage A) — the user supplies a (playerId, playerToken) obtained some
/// other way (a community pairing tool, or eventually this app's own Stage B auto-pairing once
/// that's working). Once paired, this exposes GetTeamInfo and vending-machine map markers live
/// from the game server. See docs/ARCHITECTURE.md for the honesty note on what's verified vs.
/// best-effort in this subsystem.
/// </summary>
[ApiController]
[Route("api/servers/{serverId:guid}/rustplus")]
[Authorize]
public class RustPlusController : ControllerBase
{
    private const int VendingMachineMarkerType = 3;

    private readonly AppDbContext _db;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly RustPlusAutoPairingSession _autoPairingSession;
    private readonly RustPlusOptions _rustPlusOptions;
    private readonly IEncryptionService? _encryption;

    public RustPlusController(
        AppDbContext db,
        RustPlusConnectionManager connectionManager,
        RustPlusAutoPairingSession autoPairingSession,
        IOptions<RustPlusOptions> rustPlusOptions,
        IEncryptionService? encryption = null)
    {
        _db = db;
        _connectionManager = connectionManager;
        _autoPairingSession = autoPairingSession;
        _rustPlusOptions = rustPlusOptions.Value;
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
    /// Experimental — see docs/RUSTPLUS.md. Opens an FCM/MCS session and waits (up to 2 minutes)
    /// for the player to run "app.pair" while looking at this server in-game, then saves the
    /// resulting pairing automatically. Requires RustPlus:FcmSenderId to be configured and
    /// RustPlus:EnableAutoPairing to be true. The caller must supply a Steam auth session ticket
    /// obtained from their own Steam client — this endpoint cannot mint one itself.
    /// </summary>
    [HttpPost("auto-pair")]
    public async Task<ActionResult<RustPlusPairingResponse>> AutoPair(Guid serverId, [FromBody] AutoPairRequest request, CancellationToken ct)
    {
        if (!_rustPlusOptions.EnableAutoPairing)
            return StatusCode(503, "Auto-pairing is disabled (RustPlus:EnableAutoPairing). It's experimental and unverified — see docs/RUSTPLUS.md. Use manual pairing (POST pairing) instead.");

        if (_encryption is null)
            return StatusCode(503, "This server has no Encryption:FieldKey configured, so Rust+ tokens can't be stored securely.");

        var ownsServer = await _db.RustServers.AnyAsync(s => s.Id == serverId && s.OwnerUserId == CurrentUserId, ct);
        if (!ownsServer) return NotFound();

        RustPlusAutoPairingResult result;
        try
        {
            result = await _autoPairingSession.WaitForPairingAsync(request.SteamAuthTicketHex, TimeSpan.FromMinutes(2), ct);
        }
        catch (Exception ex)
        {
            return StatusCode(502, $"Auto-pairing failed or timed out: {ex.Message}. See docs/RUSTPLUS.md — this path is experimental.");
        }

        var userId = CurrentUserId;
        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.ServerId == serverId && p.UserId == userId, ct);
        if (pairing is null)
        {
            pairing = new RustPlusPairing { UserId = userId, ServerId = serverId };
            _db.RustPlusPairings.Add(pairing);
        }
        else
        {
            await _connectionManager.DropAsync(pairing.Id);
        }

        pairing.PlayerId = result.PlayerId;
        pairing.PlayerTokenEncrypted = _encryption.Encrypt(result.PlayerToken);
        pairing.ServerIp = result.ServerIp;
        pairing.ServerPort = result.ServerPort;

        await _db.SaveChangesAsync(ct);
        return ToResponse(pairing);
    }

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
