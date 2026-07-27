using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rustex.Api.Auth;
using Rustex.Domain.Billing;
using Rustex.Api.Dtos;
using Rustex.Domain.Entities;
using Rustex.Domain.RustPlus;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Controllers;

/// <summary>
/// CRUD for a user's Shop Alerts on one server — RustPlusVendingPollWorker is what actually
/// evaluates and fires these against live vending diffs.
/// </summary>
[ApiController]
[Route("api/servers/{serverId:guid}/rustplus/shop-alerts")]
[Authorize]
[RequiresFeature(Features.ShopAlerts)]
public class RustPlusShopAlertsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IRustItemCatalog _catalog;

    public RustPlusShopAlertsController(AppDbContext db, IRustItemCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<List<ShopAlertResponse>>> List(Guid serverId, CancellationToken ct)
    {
        var alerts = await _db.ShopAlerts
            .Where(a => a.ServerId == serverId && a.UserId == CurrentUserId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        return alerts.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<ShopAlertResponse>> Create(Guid serverId, [FromBody] CreateShopAlertRequest request, CancellationToken ct)
    {
        if (request.ItemId is null && string.IsNullOrWhiteSpace(request.ItemNameContains))
            return BadRequest("Provide either itemId or itemNameContains.");

        var ownsPairing = await _db.RustPlusPairings.AnyAsync(p => p.ServerId == serverId && p.UserId == CurrentUserId, ct);
        if (!ownsPairing) return NotFound("No Rust+ pairing saved for this server yet.");

        var alert = new ShopAlert
        {
            UserId = CurrentUserId,
            ServerId = serverId,
            ItemId = request.ItemId,
            ItemNameContains = request.ItemNameContains,
            MaxCostPerItem = request.MaxCostPerItem,
            MinAmountInStock = Math.Max(1, request.MinAmountInStock),
            NotifyOnNewListing = request.NotifyOnNewListing,
            NotifyOnPriceDrop = request.NotifyOnPriceDrop,
            NotifyOnRestock = request.NotifyOnRestock,
            CooldownSeconds = Math.Max(60, request.CooldownSeconds),
        };
        _db.ShopAlerts.Add(alert);
        await _db.SaveChangesAsync(ct);
        return ToResponse(alert);
    }

    [HttpPut("{alertId:guid}")]
    public async Task<ActionResult<ShopAlertResponse>> Update(Guid serverId, Guid alertId, [FromBody] UpdateShopAlertRequest request, CancellationToken ct)
    {
        var alert = await _db.ShopAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.ServerId == serverId && a.UserId == CurrentUserId, ct);
        if (alert is null) return NotFound();

        if (request.ItemId is null && string.IsNullOrWhiteSpace(request.ItemNameContains))
            return BadRequest("Provide either itemId or itemNameContains.");

        alert.ItemId = request.ItemId;
        alert.ItemNameContains = request.ItemNameContains;
        alert.MaxCostPerItem = request.MaxCostPerItem;
        alert.MinAmountInStock = Math.Max(1, request.MinAmountInStock);
        alert.NotifyOnNewListing = request.NotifyOnNewListing;
        alert.NotifyOnPriceDrop = request.NotifyOnPriceDrop;
        alert.NotifyOnRestock = request.NotifyOnRestock;
        alert.IsEnabled = request.IsEnabled;
        alert.CooldownSeconds = Math.Max(60, request.CooldownSeconds);

        await _db.SaveChangesAsync(ct);
        return ToResponse(alert);
    }

    [HttpDelete("{alertId:guid}")]
    public async Task<IActionResult> Delete(Guid serverId, Guid alertId, CancellationToken ct)
    {
        var alert = await _db.ShopAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.ServerId == serverId && a.UserId == CurrentUserId, ct);
        if (alert is null) return NotFound();

        _db.ShopAlerts.Remove(alert);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ShopAlertResponse ToResponse(ShopAlert a) => new(
        a.Id, a.ServerId, a.ItemId, a.ItemId is not null ? _catalog.Find(a.ItemId.Value)?.Name : null, a.ItemNameContains,
        a.MaxCostPerItem, a.MinAmountInStock, a.NotifyOnNewListing, a.NotifyOnPriceDrop, a.NotifyOnRestock,
        a.IsEnabled, a.CooldownSeconds, a.LastTriggeredAt, a.CreatedAt);
}
