namespace Rustex.Domain.RustPlus;

public enum VendingChangeKind
{
    ListingAppeared,
    PriceDropped,
    Restocked,
    SoldOut,
    MachineDisappeared,
}

/// <summary>One vending machine's state at a point in time, decoupled from the Rust+ protobuf
/// types so this whole diffing engine stays framework-free and unit-testable without a live
/// connection — the shop-alert poller maps AppMarker/SellOrder into this before calling Compute.</summary>
public sealed record VendingListingState(int ItemId, int CurrencyId, int CostPerItem, int AmountInStock);

public sealed record VendingMachineState(int MarkerId, IReadOnlyList<VendingListingState> Listings);

/// <summary>ItemId is null for MachineDisappeared — that event isn't about any one listing.</summary>
public sealed record VendingChange(
    VendingChangeKind Kind,
    int MarkerId,
    int? ItemId,
    int? OldCostPerItem,
    int? NewCostPerItem,
    int? OldAmountInStock,
    int? NewAmountInStock);

/// <summary>Diffs two snapshots of a server's vending machines to find what a Shop Alert should
/// fire on. Deliberately narrow: a price *rise* is not an event (nobody wants an alert for
/// something getting more expensive), and a listing simply vanishing from a machine that's still
/// there isn't one of the five kinds either — only what's listed below.</summary>
public static class VendingDiff
{
    public static IReadOnlyList<VendingChange> Compute(
        IReadOnlyList<VendingMachineState> previous,
        IReadOnlyList<VendingMachineState> current)
    {
        var changes = new List<VendingChange>();
        var previousByMarker = previous.ToDictionary(m => m.MarkerId);
        var currentMarkerIds = current.Select(m => m.MarkerId).ToHashSet();

        foreach (var machine in current)
        {
            var previousListings = previousByMarker.TryGetValue(machine.MarkerId, out var prevMachine)
                ? prevMachine.Listings.ToDictionary(l => l.ItemId)
                : new Dictionary<int, VendingListingState>();

            foreach (var listing in machine.Listings)
            {
                if (!previousListings.TryGetValue(listing.ItemId, out var previousListing))
                {
                    changes.Add(new VendingChange(VendingChangeKind.ListingAppeared, machine.MarkerId, listing.ItemId,
                        null, listing.CostPerItem, null, listing.AmountInStock));
                    continue;
                }

                if (listing.CostPerItem < previousListing.CostPerItem)
                {
                    changes.Add(new VendingChange(VendingChangeKind.PriceDropped, machine.MarkerId, listing.ItemId,
                        previousListing.CostPerItem, listing.CostPerItem, previousListing.AmountInStock, listing.AmountInStock));
                }

                if (previousListing.AmountInStock == 0 && listing.AmountInStock > 0)
                {
                    changes.Add(new VendingChange(VendingChangeKind.Restocked, machine.MarkerId, listing.ItemId,
                        previousListing.CostPerItem, listing.CostPerItem, previousListing.AmountInStock, listing.AmountInStock));
                }
                else if (previousListing.AmountInStock > 0 && listing.AmountInStock == 0)
                {
                    changes.Add(new VendingChange(VendingChangeKind.SoldOut, machine.MarkerId, listing.ItemId,
                        previousListing.CostPerItem, listing.CostPerItem, previousListing.AmountInStock, listing.AmountInStock));
                }
            }
        }

        foreach (var machine in previous)
        {
            if (!currentMarkerIds.Contains(machine.MarkerId))
                changes.Add(new VendingChange(VendingChangeKind.MachineDisappeared, machine.MarkerId, null, null, null, null, null));
        }

        return changes;
    }
}
