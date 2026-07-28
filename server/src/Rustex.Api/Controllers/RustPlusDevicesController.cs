using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Auth;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus;
using Rustex.Infrastructure.Security;

namespace Rustex.Api.Controllers;

/// <summary>
/// Smart Switch/Alarm/Storage Monitor CRUD + live control. Rows are normally populated by
/// RustPlusSmartDevicesWorker from FCM pairing pushes; POST here covers manual entry for anyone
/// not running the auto-pairing helper, matching how server/team pairing already keeps a manual
/// fallback.
/// </summary>
[ApiController]
[Route("api/servers/{serverId:guid}/rustplus/devices")]
[Authorize]
public class RustPlusDevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly IEncryptionService? _encryption;

    public RustPlusDevicesController(AppDbContext db, RustPlusConnectionManager connectionManager, IEncryptionService? encryption = null)
    {
        _db = db;
        _connectionManager = connectionManager;
        _encryption = encryption;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<RustPlusSmartDeviceResponse>>> List(Guid serverId, CancellationToken ct)
    {
        var devices = await _db.RustPlusSmartDevices
            .Where(d => d.ServerId == serverId && d.UserId == CurrentUserId)
            .OrderBy(d => d.Type).ThenBy(d => d.Name)
            .ToListAsync(ct);
        return devices.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<RustPlusSmartDeviceResponse>> Create(Guid serverId, [FromBody] CreateSmartDeviceRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<SmartDeviceKind>(request.Type, true, out var kind))
            return BadRequest("type must be Switch, Alarm, or StorageMonitor.");

        var ownsPairing = await _db.RustPlusPairings.AnyAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (!ownsPairing) return NotFound("No Rust+ pairing saved for this server yet.");

        var existing = await _db.RustPlusSmartDevices.FirstOrDefaultAsync(d => d.ServerId == serverId && d.EntityId == request.EntityId, ct);
        if (existing is not null) return Conflict("A device with this entity id is already registered.");

        var device = new RustPlusSmartDevice
        {
            UserId = CurrentUserId,
            ServerId = serverId,
            EntityId = request.EntityId,
            Type = kind,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{kind} #{request.EntityId}" : request.Name,
        };
        _db.RustPlusSmartDevices.Add(device);
        await _db.SaveChangesAsync(ct);
        return ToResponse(device);
    }

    [HttpPut("{deviceId:guid}")]
    public async Task<ActionResult<RustPlusSmartDeviceResponse>> Update(Guid serverId, Guid deviceId, [FromBody] UpdateSmartDeviceRequest request, CancellationToken ct)
    {
        var device = await _db.RustPlusSmartDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.ServerId == serverId && d.UserId == CurrentUserId, ct);
        if (device is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name)) device.Name = request.Name;
        device.AlarmRaisesRaidEvent = request.AlarmRaisesRaidEvent;
        await _db.SaveChangesAsync(ct);
        return ToResponse(device);
    }

    [HttpDelete("{deviceId:guid}")]
    public async Task<IActionResult> Delete(Guid serverId, Guid deviceId, CancellationToken ct)
    {
        var device = await _db.RustPlusSmartDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.ServerId == serverId && d.UserId == CurrentUserId, ct);
        if (device is null) return NotFound();

        _db.RustPlusSmartDevices.Remove(device);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Toggles a Smart Switch live. Alarms and Storage Monitors are read-only in Rust+
    /// itself — Facepunch doesn't expose a way to set their value, only switches.</summary>
    [HttpPost("{deviceId:guid}/value")]
    public async Task<IActionResult> SetValue(Guid serverId, Guid deviceId, [FromBody] SetSmartDeviceValueRequest request, CancellationToken ct)
    {
        if (_encryption is null) return StatusCode(503, "This server has no Encryption:FieldKey configured.");

        var device = await _db.RustPlusSmartDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.ServerId == serverId && d.UserId == CurrentUserId, ct);
        if (device is null) return NotFound();
        if (device.Type != SmartDeviceKind.Switch) return BadRequest("Only Smart Switches can be set — Alarms and Storage Monitors are read-only.");

        var pairing = await _db.RustPlusPairings.FirstOrDefaultAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (pairing is null) return NotFound("No Rust+ pairing saved for this server yet.");
        if (!int.TryParse(_encryption.Decrypt(pairing.PlayerTokenEncrypted), out var token))
            return Conflict("The saved Rust+ token for this pairing is malformed — delete and re-pair this server.");

        try
        {
            var client = await _connectionManager.GetOrConnectAsync(pairing, token, ct);
            await client.SetEntityValueAsync((uint)device.EntityId, request.Value, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(502, $"Rust+ request failed: {ex.Message}");
        }

        device.LastKnownValue = request.Value;
        device.LastChangedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static RustPlusSmartDeviceResponse ToResponse(RustPlusSmartDevice d) => new(
        d.Id, d.EntityId, d.Type.ToString(), d.Name, d.LastKnownValue, d.LastKnownCapacity,
        d.AlarmRaisesRaidEvent, d.LastChangedAt, d.PairedAt);
}
