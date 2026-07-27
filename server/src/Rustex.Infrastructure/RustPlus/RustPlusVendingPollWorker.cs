using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rustex.Domain;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;
using Rustex.Domain.RustPlus;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.RustPlus.Proto;

namespace Rustex.Infrastructure.RustPlus;

/// <summary>
/// Polls getMapMarkers for every actively-connected pairing, keeps VendingMachineSnapshot /
/// VendingListing in sync (so vending search reads the DB, never the game server, on every
/// keystroke), and drives Shop Alerts off the diff. Runs for every connected session rather than
/// only ones with an enabled ShopAlert — vending search needs fresh data independent of whether
/// alerts are configured. getMapMarkers returns a marker per online player too, which can be a
/// few hundred KB on a full server; a longer interval here trades alert latency for bandwidth.
/// </summary>
public class RustPlusVendingPollWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private const int DefaultMapSize = 4000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RustPlusConnectionManager _connectionManager;
    private readonly ILogger<RustPlusVendingPollWorker> _logger;

    public RustPlusVendingPollWorker(IServiceScopeFactory scopeFactory, RustPlusConnectionManager connectionManager, ILogger<RustPlusVendingPollWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            foreach (var pairingId in _connectionManager.ActiveSessionIds)
            {
                try
                {
                    await PollPairingAsync(pairingId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Vending poll failed for pairing {PairingId}", pairingId);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PollPairingAsync(Guid pairingId, CancellationToken ct)
    {
        if (!_connectionManager.TryGetClient(pairingId, out var client) || client is null)
            return; // mid-reconnect — next tick will catch up

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pairing = await db.RustPlusPairings.Include(p => p.Server)
            .FirstOrDefaultAsync(p => p.Id == pairingId, ct);
        if (pairing is null) return;

        IReadOnlyList<AppMarker> markers;
        try
        {
            markers = await client.GetMapMarkersAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "getMapMarkers failed for pairing {PairingId}", pairingId);
            return;
        }

        var vendingMarkers = markers.Where(m => m.Type == AppMarkerType.VendingMachine).ToList();
        var mapSize = pairing.Server.WorldSize ?? DefaultMapSize;

        var existingSnapshots = await db.VendingMachineSnapshots
            .Include(s => s.Listings)
            .Where(s => s.ServerId == pairing.ServerId)
            .ToListAsync(ct);

        var previousStates = existingSnapshots
            .Select(s => new VendingMachineState(s.MarkerId, s.Listings.Select(l => new VendingListingState(l.ItemId, l.CurrencyId, l.CostPerItem, l.AmountInStock)).ToList()))
            .ToList();
        var currentStates = vendingMarkers
            .Select(m => new VendingMachineState((int)m.Id, m.SellOrders.Select(o => new VendingListingState(o.ItemId, o.CurrencyId, o.CostPerItem, o.AmountInStock)).ToList()))
            .ToList();

        var changes = VendingDiff.Compute(previousStates, currentStates);

        var now = DateTimeOffset.UtcNow;
        var snapshotsByMarker = existingSnapshots.ToDictionary(s => s.MarkerId);
        var currentMarkerIds = vendingMarkers.Select(m => (int)m.Id).ToHashSet();

        foreach (var marker in vendingMarkers)
        {
            var markerId = (int)marker.Id;
            if (!snapshotsByMarker.TryGetValue(markerId, out var snapshot))
            {
                snapshot = new VendingMachineSnapshot { ServerId = pairing.ServerId, MarkerId = markerId, FirstSeenAt = now };
                db.VendingMachineSnapshots.Add(snapshot);
                snapshotsByMarker[markerId] = snapshot;
            }

            snapshot.X = marker.X;
            snapshot.Y = marker.Y;
            snapshot.Grid = GridConverter.ToGrid(marker.X, marker.Y, mapSize);
            snapshot.Name = string.IsNullOrWhiteSpace(marker.Name) ? snapshot.Name : marker.Name;
            snapshot.OutOfStock = marker.SellOrders.Count == 0 || marker.SellOrders.All(o => o.AmountInStock == 0);
            snapshot.LastSeenAt = now;

            var listingsByItem = snapshot.Listings.ToDictionary(l => l.ItemId);
            var currentItemIds = marker.SellOrders.Select(o => o.ItemId).ToHashSet();

            foreach (var order in marker.SellOrders)
            {
                if (!listingsByItem.TryGetValue(order.ItemId, out var listing))
                {
                    listing = new VendingListing { SnapshotId = snapshot.Id, ItemId = order.ItemId };
                    snapshot.Listings.Add(listing);
                }
                listing.Quantity = order.Quantity;
                listing.CurrencyId = order.CurrencyId;
                listing.CostPerItem = order.CostPerItem;
                listing.AmountInStock = order.AmountInStock;
                listing.ItemIsBlueprint = order.ItemIsBlueprint;
                listing.CurrencyIsBlueprint = order.CurrencyIsBlueprint;
                listing.UpdatedAt = now;
            }

            foreach (var stale in snapshot.Listings.Where(l => !currentItemIds.Contains(l.ItemId)).ToList())
                db.VendingListings.Remove(stale);
        }

        foreach (var gone in existingSnapshots.Where(s => !currentMarkerIds.Contains(s.MarkerId)))
            db.VendingMachineSnapshots.Remove(gone);

        var markerGrids = snapshotsByMarker.ToDictionary(kv => kv.Key, kv => kv.Value.Grid);

        await db.SaveChangesAsync(ct);

        if (changes.Count > 0)
            await DispatchShopAlertsAsync(db, scope.ServiceProvider.GetRequiredService<INotificationDispatcher>(), scope.ServiceProvider.GetRequiredService<IRustItemCatalog>(), pairing, changes, markerGrids, ct);
    }

    private static async Task DispatchShopAlertsAsync(
        AppDbContext db, INotificationDispatcher dispatcher, IRustItemCatalog catalog,
        RustPlusPairing pairing, IReadOnlyList<VendingChange> changes, IReadOnlyDictionary<int, string?> markerGrids, CancellationToken ct)
    {
        var alerts = await db.ShopAlerts.Where(a => a.ServerId == pairing.ServerId && a.IsEnabled).ToListAsync(ct);
        if (alerts.Count == 0) return;

        var now = DateTimeOffset.UtcNow;

        foreach (var change in changes)
        {
            if (change.ItemId is null) continue; // MachineDisappeared / SoldOut with no item context isn't a ShopAlert trigger

            var item = catalog.Find(change.ItemId.Value);

            foreach (var alert in alerts)
            {
                var kindEnabled = change.Kind switch
                {
                    VendingChangeKind.ListingAppeared => alert.NotifyOnNewListing,
                    VendingChangeKind.PriceDropped => alert.NotifyOnPriceDrop,
                    VendingChangeKind.Restocked => alert.NotifyOnRestock,
                    _ => false,
                };
                if (!kindEnabled) continue;

                if (alert.ItemId is not null && alert.ItemId != change.ItemId) continue;
                if (alert.ItemNameContains is not null &&
                    !(item?.Name.Contains(alert.ItemNameContains, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                if (alert.MaxCostPerItem is not null && change.NewCostPerItem > alert.MaxCostPerItem) continue;
                if (change.NewAmountInStock is not null && change.NewAmountInStock < alert.MinAmountInStock) continue;
                if (alert.LastTriggeredAt is not null && now - alert.LastTriggeredAt < TimeSpan.FromSeconds(alert.CooldownSeconds)) continue;

                var itemName = item?.Name ?? $"item {change.ItemId}";
                var grid = markerGrids.TryGetValue(change.MarkerId, out var g) ? g : null;
                var location = grid is null ? pairing.Server.Name : $"{pairing.Server.Name} ({grid})";
                var body = change.Kind switch
                {
                    VendingChangeKind.ListingAppeared => $"{itemName} listed for {change.NewCostPerItem} on {location}.",
                    VendingChangeKind.PriceDropped => $"{itemName} dropped to {change.NewCostPerItem} (was {change.OldCostPerItem}) on {location}.",
                    VendingChangeKind.Restocked => $"{itemName} restocked ({change.NewAmountInStock} left) on {location}.",
                    _ => $"{itemName} updated on {location}.",
                };

                await dispatcher.DispatchAsync(new DispatchNotificationRequest(
                    UserId: alert.UserId,
                    Type: "ShopAlert",
                    Title: itemName,
                    Body: body,
                    Severity: NotificationSeverity.Info,
                    ServerId: pairing.ServerId,
                    WebhookEventType: "ShopAlert",
                    RelatedEntityType: "ShopAlert",
                    RelatedEntityId: alert.Id), ct);

                alert.LastTriggeredAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
