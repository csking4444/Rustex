using Rustex.Domain.Billing;
using Rustex.Domain.Entities;
using Xunit;

namespace Rustex.Api.Tests.Billing;

/// <summary>You get exactly the tier you paid for.
///
/// The property under test is where the tier comes from: <b>the price the provider says was
/// actually charged</b>, resolved server-side, not anything the browser sent. That is what stops
/// someone paying $4.99 for Scout and receiving Clan, and it is why the webhook re-reads the
/// subscription from Stripe rather than trusting the checkout request that started it.</summary>
public class PurchaseToEntitlementTests
{
    /// <summary>Simulates a completed purchase: the customer paid for <paramref name="priceId"/>,
    /// Stripe reports it, and the webhook syncs. Mirrors what
    /// <c>BillingWebhookController</c> does on <c>checkout.session.completed</c>.</summary>
    private static async Task<Entitlement> PurchaseAsync(BillingTestHarness h, string priceId)
    {
        // A checkout has started, so a row exists holding the provider customer — but with no
        // tier granted yet (Incomplete does not entitle).
        h.Db.Subscriptions.Add(new Subscription
        {
            UserId = h.User.Id,
            PlanTier = PlanCatalog.Scout,
            Status = SubscriptionStatus.Incomplete,
            Source = SubscriptionSource.Stripe,
            ProviderCustomerId = "cus_test",
            ProviderSubscriptionId = "sub_test",
        });
        h.Db.SaveChanges();

        h.Provider.Next = h.Provider.Next with { PriceId = priceId, Status = SubscriptionStatus.Active };
        await h.Service.SyncFromProviderAsync("sub_test", DateTimeOffset.UtcNow, default);

        h.Db.ChangeTracker.Clear();
        return await h.Service.GetEntitlementAsync(h.User.Id, default);
    }

    [Theory]
    [InlineData("price_scout_m", PlanCatalog.Scout, 2, 5)]
    [InlineData("price_scout_y", PlanCatalog.Scout, 2, 5)]
    [InlineData("price_raider_m", PlanCatalog.Raider, 10, 20)]
    [InlineData("price_raider_y", PlanCatalog.Raider, 10, 20)]
    [InlineData("price_clan_m", PlanCatalog.Clan, 25, 100)]
    [InlineData("price_clan_y", PlanCatalog.Clan, 25, 100)]
    public async Task PayingForAPrice_GrantsThatTiersLimits(
        string priceId, string expectedTier, int servers, int members)
    {
        using var h = new BillingTestHarness();

        var entitlement = await PurchaseAsync(h, priceId);

        Assert.True(entitlement.IsEntitled);
        Assert.Equal(expectedTier, entitlement.Tier);
        Assert.Equal(servers, entitlement.ServerLimit);
        Assert.Equal(members, entitlement.TeamMemberLimit);
        Assert.False(entitlement.IsComplimentary); // a purchase, not a grant
    }

    [Theory]
    [InlineData("price_scout_m", Features.RaidAlarms, true)]
    [InlineData("price_scout_m", Features.SmartDevices, false)]   // Scout stops short of these
    [InlineData("price_scout_m", Features.Analytics, false)]
    [InlineData("price_raider_m", Features.SmartDevices, true)]   // Raider adds the market tools
    [InlineData("price_raider_m", Features.ShopAlerts, true)]
    [InlineData("price_raider_m", Features.Analytics, false)]     // but not analytics
    [InlineData("price_clan_m", Features.Analytics, true)]        // Clan is everything
    [InlineData("price_clan_m", Features.ChatAssistant, true)]
    public async Task PayingForAPrice_UnlocksExactlyThatTiersFeatures(
        string priceId, string feature, bool expected)
    {
        using var h = new BillingTestHarness();

        var entitlement = await PurchaseAsync(h, priceId);

        Assert.Equal(expected, entitlement.Allows(feature));
    }

    /// <summary>The row starts out claiming Scout. Paying for Clan must move it up — proving the
    /// tier is taken from the price charged, not from whatever the record happened to say.</summary>
    [Fact]
    public async Task TierComesFromThePriceCharged_NotTheExistingRow()
    {
        using var h = new BillingTestHarness();

        var entitlement = await PurchaseAsync(h, "price_clan_y");

        Assert.Equal(PlanCatalog.Clan, entitlement.Tier);
        Assert.Equal(BillingInterval.Yearly, (await h.Service.GetSubscriptionAsync(h.User.Id, default))!.Interval);
    }

    /// <summary>A price we do not recognise must not be interpreted generously. This is the case
    /// where someone is charged through a price created outside our configuration.</summary>
    [Fact]
    public async Task UnrecognisedPrice_DoesNotUpgradeAnyone()
    {
        using var h = new BillingTestHarness();
        h.Db.Subscriptions.Add(new Subscription
        {
            UserId = h.User.Id,
            PlanTier = PlanCatalog.Scout,
            Status = SubscriptionStatus.Active,
            Source = SubscriptionSource.Stripe,
            ProviderCustomerId = "cus_test",
            ProviderSubscriptionId = "sub_test",
        });
        h.Db.SaveChanges();

        h.Provider.Next = h.Provider.Next with { PriceId = "price_someone_elses_product" };
        await h.Service.SyncFromProviderAsync("sub_test", DateTimeOffset.UtcNow, default);

        h.Db.ChangeTracker.Clear();
        // Falls back to the tier already on record rather than guessing a higher one.
        var entitlement = await h.Service.GetEntitlementAsync(h.User.Id, default);
        Assert.Equal(PlanCatalog.Scout, entitlement.Tier);
    }

    /// <summary>Payment stopping must remove access. Covers the refund/chargeback path, where the
    /// subscription is cancelled at the provider and the webhook tells us.</summary>
    [Theory]
    [InlineData(SubscriptionStatus.Canceled)]
    [InlineData(SubscriptionStatus.Unpaid)]
    public async Task WhenPaymentStops_AccessIsRemoved(SubscriptionStatus terminal)
    {
        using var h = new BillingTestHarness();
        var entitlement = await PurchaseAsync(h, "price_clan_m");
        Assert.True(entitlement.IsEntitled);

        h.Provider.Next = h.Provider.Next with { Status = terminal };
        await h.Service.SyncFromProviderAsync("sub_test", DateTimeOffset.UtcNow.AddMinutes(1), default);

        h.Db.ChangeTracker.Clear();
        Assert.False((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }

    /// <summary>Checkout must ask for the price matching the tier the customer picked — the other
    /// half of the mapping. Getting this backwards would charge the wrong amount.</summary>
    [Theory]
    [InlineData(PlanCatalog.Scout, BillingInterval.Monthly, "price_scout_m")]
    [InlineData(PlanCatalog.Scout, BillingInterval.Yearly, "price_scout_y")]
    [InlineData(PlanCatalog.Raider, BillingInterval.Monthly, "price_raider_m")]
    [InlineData(PlanCatalog.Clan, BillingInterval.Yearly, "price_clan_y")]
    public async Task Checkout_UsesThePriceForTheChosenTier(
        string tier, BillingInterval interval, string expectedPriceId)
    {
        using var h = new BillingTestHarness();

        var url = await h.Service.StartCheckoutAsync(h.User.Id, tier, interval, default);

        // FakePaymentProvider echoes the price id into the checkout URL.
        Assert.Contains(expectedPriceId, url);
    }
}
