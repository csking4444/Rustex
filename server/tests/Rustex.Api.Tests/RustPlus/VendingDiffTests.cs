using Rustex.Domain.RustPlus;
using Xunit;

namespace Rustex.Api.Tests.RustPlus;

public class VendingDiffTests
{
    private static VendingMachineState Machine(int markerId, params VendingListingState[] listings) =>
        new(markerId, listings);

    private static VendingListingState Listing(int itemId, int cost, int stock, int currency = -1) =>
        new(itemId, currency, cost, stock);

    [Fact]
    public void NewListingOnExistingMachine_IsListingAppeared()
    {
        var previous = new[] { Machine(1, Listing(itemId: 100, cost: 50, stock: 10)) };
        var current = new[] { Machine(1, Listing(100, 50, 10), Listing(200, 30, 5)) };

        var changes = VendingDiff.Compute(previous, current);

        var change = Assert.Single(changes);
        Assert.Equal(VendingChangeKind.ListingAppeared, change.Kind);
        Assert.Equal(200, change.ItemId);
        Assert.Equal(30, change.NewCostPerItem);
    }

    [Fact]
    public void NewMachineWithListings_EachListingIsAppeared()
    {
        var previous = Array.Empty<VendingMachineState>();
        var current = new[] { Machine(1, Listing(100, 50, 10), Listing(200, 30, 5)) };

        var changes = VendingDiff.Compute(previous, current);

        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(VendingChangeKind.ListingAppeared, c.Kind));
    }

    [Fact]
    public void CheaperPrice_IsPriceDropped()
    {
        var previous = new[] { Machine(1, Listing(100, cost: 100, stock: 5)) };
        var current = new[] { Machine(1, Listing(100, cost: 80, stock: 5)) };

        var changes = VendingDiff.Compute(previous, current);

        var change = Assert.Single(changes);
        Assert.Equal(VendingChangeKind.PriceDropped, change.Kind);
        Assert.Equal(100, change.OldCostPerItem);
        Assert.Equal(80, change.NewCostPerItem);
    }

    [Fact]
    public void MoreExpensivePrice_ProducesNoChange()
    {
        // This is the one that would have been trivially easy to get backwards — a price rise
        // must never fire a "shop alert", it's the opposite of something a player wants pinged for.
        var previous = new[] { Machine(1, Listing(100, cost: 80, stock: 5)) };
        var current = new[] { Machine(1, Listing(100, cost: 120, stock: 5)) };

        var changes = VendingDiff.Compute(previous, current);

        Assert.Empty(changes);
    }

    [Fact]
    public void SamePriceAndStock_ProducesNoChange()
    {
        var previous = new[] { Machine(1, Listing(100, cost: 80, stock: 5)) };
        var current = new[] { Machine(1, Listing(100, cost: 80, stock: 5)) };

        Assert.Empty(VendingDiff.Compute(previous, current));
    }

    [Fact]
    public void StockDropsToZero_IsSoldOut()
    {
        var previous = new[] { Machine(1, Listing(100, cost: 80, stock: 5)) };
        var current = new[] { Machine(1, Listing(100, cost: 80, stock: 0)) };

        var change = Assert.Single(VendingDiff.Compute(previous, current));
        Assert.Equal(VendingChangeKind.SoldOut, change.Kind);
    }

    [Fact]
    public void StockRisesFromZero_IsRestocked()
    {
        var previous = new[] { Machine(1, Listing(100, cost: 80, stock: 0)) };
        var current = new[] { Machine(1, Listing(100, cost: 80, stock: 20)) };

        var change = Assert.Single(VendingDiff.Compute(previous, current));
        Assert.Equal(VendingChangeKind.Restocked, change.Kind);
    }

    [Fact]
    public void RestockWithCheaperPrice_ProducesBothEvents()
    {
        var previous = new[] { Machine(1, Listing(100, cost: 80, stock: 0)) };
        var current = new[] { Machine(1, Listing(100, cost: 60, stock: 20)) };

        var changes = VendingDiff.Compute(previous, current);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Kind == VendingChangeKind.Restocked);
        Assert.Contains(changes, c => c.Kind == VendingChangeKind.PriceDropped);
    }

    [Fact]
    public void MachineNoLongerPresent_IsMachineDisappeared()
    {
        var previous = new[] { Machine(1, Listing(100, 80, 5)), Machine(2, Listing(200, 10, 1)) };
        var current = new[] { Machine(2, Listing(200, 10, 1)) };

        var change = Assert.Single(VendingDiff.Compute(previous, current));
        Assert.Equal(VendingChangeKind.MachineDisappeared, change.Kind);
        Assert.Equal(1, change.MarkerId);
        Assert.Null(change.ItemId);
    }

    [Fact]
    public void MarkerIdReusedAfterWipe_WithCompletelyDifferentListings_DoesNotThrowOrMisreport()
    {
        // Marker ids aren't guaranteed stable across wipes — a fresh machine can land on the same
        // marker id an old one used. The diff has no wipe-awareness; it just compares by item id
        // within the marker, so a wholesale item swap should read as plain "new listings",
        // not as price/stock changes on items that aren't actually related.
        var previous = new[] { Machine(1, Listing(itemId: 100, cost: 80, stock: 5)) };
        var current = new[] { Machine(1, Listing(itemId: 999, cost: 1, stock: 500)) };

        var changes = VendingDiff.Compute(previous, current);

        var change = Assert.Single(changes);
        Assert.Equal(VendingChangeKind.ListingAppeared, change.Kind);
        Assert.Equal(999, change.ItemId);
    }

    [Fact]
    public void EmptyPreviousAndCurrent_ProducesNoChanges()
    {
        Assert.Empty(VendingDiff.Compute([], []));
    }
}
