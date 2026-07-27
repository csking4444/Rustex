using Rustex.Domain.Billing;
using Rustex.Domain.Entities;
using Xunit;

namespace Rustex.Api.Tests.Billing;

public class EntitlementTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.Trialing, true)]
    // Still entitled: the provider is retrying the card, and locking someone out mid-retry loses
    // customers who would have paid.
    [InlineData(SubscriptionStatus.PastDue, true)]
    [InlineData(SubscriptionStatus.Canceled, false)]
    [InlineData(SubscriptionStatus.Unpaid, false)]
    [InlineData(SubscriptionStatus.Incomplete, false)]
    [InlineData(SubscriptionStatus.Paused, false)]
    public async Task Status_DecidesEntitlement(SubscriptionStatus status, bool expected)
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(status: status);

        var entitlement = await h.Service.GetEntitlementAsync(h.User.Id, default);

        Assert.Equal(expected, entitlement.IsEntitled);
    }

    [Fact]
    public async Task NoSubscription_IsNotEntitled()
    {
        using var h = new BillingTestHarness();

        var entitlement = await h.Service.GetEntitlementAsync(h.User.Id, default);

        Assert.False(entitlement.IsEntitled);
        Assert.Null(entitlement.Tier);
        Assert.Equal(0, entitlement.ServerLimit);
    }

    /// <summary>A row pointing at a tier we no longer sell must not fall through to full access.</summary>
    [Fact]
    public async Task UnknownTier_GrantsNothing()
    {
        using var h = new BillingTestHarness();
        var sub = h.AddPaidSubscription();
        sub.PlanTier = "legacy_gold";
        h.Db.SaveChanges();

        var entitlement = await h.Service.GetEntitlementAsync(h.User.Id, default);

        Assert.False(entitlement.IsEntitled);
    }

    [Fact]
    public async Task Entitlement_CarriesPlanLimitsAndFeatures()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(PlanCatalog.Scout);

        var entitlement = await h.Service.GetEntitlementAsync(h.User.Id, default);

        Assert.Equal(2, entitlement.ServerLimit);
        Assert.True(entitlement.Allows(Features.RaidAlarms));
        // Scout does not include smart devices — that is what makes the tier gate meaningful.
        Assert.False(entitlement.Allows(Features.SmartDevices));
    }

    [Fact]
    public async Task Entitlement_SurvivesTheCacheRoundTrip()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(PlanCatalog.Clan);

        var first = await h.Service.GetEntitlementAsync(h.User.Id, default);
        var cached = await h.Service.GetEntitlementAsync(h.User.Id, default);

        Assert.Equal(first.Tier, cached.Tier);
        Assert.Equal(first.ServerLimit, cached.ServerLimit);
        // The cached shape swaps IReadOnlySet for an array; this is what catches that regressing.
        Assert.True(cached.Allows(Features.Analytics));
    }
}

public class PlanChangeTests
{
    [Fact]
    public async Task Upgrade_ProratesImmediately()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(PlanCatalog.Scout);

        await h.Service.ChangePlanAsync(h.User.Id, PlanCatalog.Clan, BillingInterval.Monthly, default);

        var change = Assert.Single(h.Provider.PlanChanges);
        Assert.Equal("price_clan_m", change.PriceId);
        Assert.True(change.Prorate);
    }

    /// <summary>Downgrades must not prorate — the customer already paid for the higher tier this
    /// period, and refunding it mid-cycle is not what "downgrade" means here.</summary>
    [Fact]
    public async Task Downgrade_DoesNotProrate()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(PlanCatalog.Clan);

        await h.Service.ChangePlanAsync(h.User.Id, PlanCatalog.Scout, BillingInterval.Monthly, default);

        var change = Assert.Single(h.Provider.PlanChanges);
        Assert.False(change.Prorate);
    }

    [Fact]
    public async Task MonthlyToYearlyOnSameTier_CountsAsUpgrade()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(PlanCatalog.Raider, interval: BillingInterval.Monthly);

        await h.Service.ChangePlanAsync(h.User.Id, PlanCatalog.Raider, BillingInterval.Yearly, default);

        Assert.True(Assert.Single(h.Provider.PlanChanges).Prorate);
    }

    [Fact]
    public async Task ChangingToTheSamePlan_IsRejected()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(PlanCatalog.Raider);

        await Assert.ThrowsAsync<SubscriptionStateException>(() =>
            h.Service.ChangePlanAsync(h.User.Id, PlanCatalog.Raider, BillingInterval.Monthly, default));
    }

    [Fact]
    public async Task UnknownTier_IsRejected()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription();

        await Assert.ThrowsAsync<SubscriptionStateException>(() =>
            h.Service.ChangePlanAsync(h.User.Id, "enterprise", BillingInterval.Monthly, default));
    }

    [Fact]
    public async Task Cancel_DefaultsToEndOfPeriod()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription();

        await h.Service.CancelAsync(h.User.Id, immediately: false, default);

        Assert.True(Assert.Single(h.Provider.Cancellations).AtPeriodEnd);
    }

    [Fact]
    public async Task Resume_RequiresAPendingCancellation()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription();

        await Assert.ThrowsAsync<SubscriptionStateException>(() =>
            h.Service.ResumeAsync(h.User.Id, default));
    }

    [Fact]
    public async Task CheckoutWhileAlreadySubscribed_IsRejected()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription();

        await Assert.ThrowsAsync<SubscriptionStateException>(() =>
            h.Service.StartCheckoutAsync(h.User.Id, PlanCatalog.Clan, BillingInterval.Monthly, default));
    }

    [Fact]
    public async Task PlanChange_InvalidatesTheEntitlementCache()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(PlanCatalog.Scout);
        await h.Service.GetEntitlementAsync(h.User.Id, default); // warm the cache

        await h.Service.ChangePlanAsync(h.User.Id, PlanCatalog.Clan, BillingInterval.Monthly, default);

        // Without the invalidation a user would keep the old tier's limits for up to the TTL,
        // which after an upgrade means paying for access they cannot use yet.
        var after = await h.Service.GetEntitlementAsync(h.User.Id, default);
        Assert.Equal(PlanCatalog.Clan, after.Tier);
    }
}

public class ComplimentaryAccessTests
{
    [Fact]
    public async Task Grant_EntitlesWithoutAnyBillingDates()
    {
        using var h = new BillingTestHarness();

        await h.Service.GrantComplimentaryAsync(h.User.Id, PlanCatalog.Clan, "storm account", default);

        var sub = await h.Service.GetSubscriptionAsync(h.User.Id, default);
        Assert.NotNull(sub);
        Assert.True(sub!.IsComplimentary);
        // The point of the whole comp path: nothing that would let the UI render a charge.
        Assert.Null(sub.CurrentPeriodEnd);
        Assert.Null(sub.CurrentPeriodStart);
        Assert.False(sub.CancelAtPeriodEnd);

        var entitlement = await h.Service.GetEntitlementAsync(h.User.Id, default);
        Assert.True(entitlement.IsEntitled);
        Assert.True(entitlement.IsComplimentary);
        Assert.Null(entitlement.CurrentPeriodEnd);
        Assert.Null(entitlement.Interval);
    }

    [Fact]
    public async Task Comped_HasNoBillingHistory()
    {
        using var h = new BillingTestHarness();
        await h.Service.GrantComplimentaryAsync(h.User.Id, PlanCatalog.Clan, null, default);

        var invoices = await h.Service.GetBillingHistoryAsync(h.User.Id, 25, default);

        Assert.Empty(invoices);
    }

    [Fact]
    public async Task Comped_CannotCancelOrChangePlan()
    {
        using var h = new BillingTestHarness();
        await h.Service.GrantComplimentaryAsync(h.User.Id, PlanCatalog.Raider, null, default);

        await Assert.ThrowsAsync<SubscriptionStateException>(() => h.Service.CancelAsync(h.User.Id, false, default));
        await Assert.ThrowsAsync<SubscriptionStateException>(() =>
            h.Service.ChangePlanAsync(h.User.Id, PlanCatalog.Clan, BillingInterval.Monthly, default));
        await Assert.ThrowsAsync<SubscriptionStateException>(() =>
            h.Service.CreatePaymentMethodUpdateUrlAsync(h.User.Id, default));
    }

    [Fact]
    public async Task Grant_RefusesToOverwriteAPaidSubscription()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription();

        await Assert.ThrowsAsync<SubscriptionStateException>(() =>
            h.Service.GrantComplimentaryAsync(h.User.Id, PlanCatalog.Clan, null, default));
    }

    [Fact]
    public async Task Revoke_RemovesEntitlement()
    {
        using var h = new BillingTestHarness();
        await h.Service.GrantComplimentaryAsync(h.User.Id, PlanCatalog.Clan, null, default);

        await h.Service.RevokeComplimentaryAsync(h.User.Id, default);

        Assert.False((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }
}

public class WebhookHandlingTests
{
    [Fact]
    public async Task ClaimingTheSameEventTwice_OnlySucceedsOnce()
    {
        using var h = new BillingTestHarness();

        var first = await h.Service.TryClaimWebhookEventAsync("evt_1", "invoice.paid", default);
        var second = await h.Service.TryClaimWebhookEventAsync("evt_1", "invoice.paid", default);

        Assert.True(first);
        // Without this, a redelivered invoice.paid would write a second invoice row.
        Assert.False(second);
    }

    [Fact]
    public async Task DistinctEvents_AreBothClaimed()
    {
        using var h = new BillingTestHarness();

        Assert.True(await h.Service.TryClaimWebhookEventAsync("evt_1", "invoice.paid", default));
        Assert.True(await h.Service.TryClaimWebhookEventAsync("evt_2", "invoice.paid", default));
    }

    [Fact]
    public async Task RecordingTheSameInvoiceTwice_UpdatesRatherThanDuplicates()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription();

        var open = new ProviderInvoice("in_1", "RX-0001", InvoiceStatus.Open, 999, 0, "usd",
            null, null, null, null, DateTimeOffset.UtcNow, null, "sub_test");
        await h.Service.RecordInvoiceAsync(open, default);
        await h.Service.RecordInvoiceAsync(open with { Status = InvoiceStatus.Paid, AmountPaidCents = 999 }, default);

        var invoices = await h.Service.GetBillingHistoryAsync(h.User.Id, 25, default);
        var invoice = Assert.Single(invoices);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(999, invoice.AmountPaidCents);
    }

    /// <summary>Providers do not promise ordering, so a late-arriving older event must not roll
    /// state backwards.</summary>
    [Fact]
    public async Task OutOfOrderEvent_DoesNotOverwriteNewerState()
    {
        using var h = new BillingTestHarness();
        var sub = h.AddPaidSubscription(PlanCatalog.Clan);
        sub.ProviderUpdatedAt = DateTimeOffset.UtcNow;
        h.Db.SaveChanges();

        h.Provider.Next = h.Provider.Next with { Status = SubscriptionStatus.Canceled };
        await h.Service.SyncFromProviderAsync("sub_test", DateTimeOffset.UtcNow.AddMinutes(-10), default);

        h.Db.ChangeTracker.Clear();
        var after = await h.Service.GetSubscriptionAsync(h.User.Id, default);
        Assert.Equal(SubscriptionStatus.Active, after!.Status);
    }

    [Fact]
    public async Task InOrderEvent_IsApplied()
    {
        using var h = new BillingTestHarness();
        var sub = h.AddPaidSubscription(PlanCatalog.Clan);
        sub.ProviderUpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        h.Db.SaveChanges();

        h.Provider.Next = h.Provider.Next with { Status = SubscriptionStatus.Canceled };
        await h.Service.SyncFromProviderAsync("sub_test", DateTimeOffset.UtcNow, default);

        h.Db.ChangeTracker.Clear();
        var after = await h.Service.GetSubscriptionAsync(h.User.Id, default);
        Assert.Equal(SubscriptionStatus.Canceled, after!.Status);
        Assert.False((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }

    /// <summary>A plan changed directly in the provider's dashboard should still land here, which
    /// only works if the price id maps back to a tier.</summary>
    [Fact]
    public async Task SyncResolvesTierFromThePriceId()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription(PlanCatalog.Scout);

        h.Provider.Next = h.Provider.Next with { PriceId = "price_clan_y" };
        await h.Service.SyncFromProviderAsync("sub_test", DateTimeOffset.UtcNow, default);

        h.Db.ChangeTracker.Clear();
        var after = await h.Service.GetSubscriptionAsync(h.User.Id, default);
        Assert.Equal(PlanCatalog.Clan, after!.PlanTier);
        Assert.Equal(BillingInterval.Yearly, after.Interval);
    }
}

public class PaymentMethodTests
{
    [Fact]
    public async Task UpsertingACard_StoresOnlyDisplayFields()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription();

        await h.Service.UpsertPaymentMethodAsync(
            "cus_test", new ProviderPaymentMethod("pm_1", "visa", "4242", 12, 2030), default);

        var card = await h.Service.GetPaymentMethodAsync(h.User.Id, default);
        Assert.NotNull(card);
        Assert.Equal("visa", card!.Brand);
        Assert.Equal("4242", card.Last4);
        Assert.True(card.IsDefault);
    }

    [Fact]
    public async Task AttachingASecondCard_DemotesTheFirst()
    {
        using var h = new BillingTestHarness();
        h.AddPaidSubscription();

        await h.Service.UpsertPaymentMethodAsync("cus_test", new ProviderPaymentMethod("pm_1", "visa", "4242", 1, 2030), default);
        await h.Service.UpsertPaymentMethodAsync("cus_test", new ProviderPaymentMethod("pm_2", "amex", "0005", 2, 2031), default);

        var card = await h.Service.GetPaymentMethodAsync(h.User.Id, default);
        Assert.Equal("0005", card!.Last4);
        Assert.Equal(1, h.Db.PaymentMethods.Count(p => p.IsDefault));
    }
}
