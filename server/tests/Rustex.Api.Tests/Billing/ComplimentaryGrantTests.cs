using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rustex.Domain.Billing;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Billing;
using Xunit;

namespace Rustex.Api.Tests.Billing;

/// <summary>The path that replaces the static site's COMPED_ACCOUNTS env var. These cover the two
/// orderings that actually happen in production: the grant configured after someone has signed
/// in, and configured before they ever have.</summary>
public class ComplimentaryGrantTests
{
    private const string StormSteamId = "76561199037012229";

    private static ComplimentaryGrantReconciler Build(BillingTestHarness h, params ComplimentaryGrant[] grants) =>
        new(h.Db, h.Service,
            Options.Create(new ComplimentaryGrantOptions { ComplimentaryGrants = grants.ToList() }),
            NullLogger<ComplimentaryGrantReconciler>.Instance);

    [Fact]
    public async Task Grant_AppliesToAMatchingAccount()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;
        h.Db.SaveChanges();

        var reconciler = Build(h, new ComplimentaryGrant { SteamId = StormSteamId, Tier = "clan", Reason = "storm account" });
        await reconciler.ReconcileAllAsync(default);

        var entitlement = await h.Service.GetEntitlementAsync(h.User.Id, default);
        Assert.True(entitlement.IsEntitled);
        Assert.Equal(PlanCatalog.Clan, entitlement.Tier);
        Assert.True(entitlement.IsComplimentary);
    }

    /// <summary>The SteamID is compared as a string end to end. Worth pinning: this value is
    /// larger than JavaScript's safe integer range, and the old site-side code produced the wrong
    /// id if it was ever handled as a number.</summary>
    [Fact]
    public async Task Grant_MatchesTheFullSeventeenDigitSteamId()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;
        h.Db.SaveChanges();

        // One digit off — the neighbouring id a float round would produce.
        await Build(h, new ComplimentaryGrant { SteamId = "76561199037012230", Tier = "clan" })
            .ReconcileAllAsync(default);

        Assert.False((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }

    [Fact]
    public async Task Grant_IsTolerantOfSurroundingWhitespace()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;
        h.Db.SaveChanges();

        await Build(h, new ComplimentaryGrant { SteamId = $"  {StormSteamId} ", Tier = "clan" })
            .ReconcileAllAsync(default);

        Assert.True((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }

    [Fact]
    public async Task NoAccountYet_IsNotAnError()
    {
        using var h = new BillingTestHarness();

        // Nobody has signed in with this SteamID; the login path retries once they do.
        await Build(h, new ComplimentaryGrant { SteamId = StormSteamId, Tier = "clan" })
            .ReconcileAllAsync(default);

        Assert.False((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }

    [Fact]
    public async Task Grant_AppliesOnFirstLoginWhenConfiguredEarlier()
    {
        using var h = new BillingTestHarness();
        var reconciler = Build(h, new ComplimentaryGrant { SteamId = StormSteamId, Tier = "clan" });

        await reconciler.ReconcileAllAsync(default);   // startup: no account yet

        h.User.SteamId = StormSteamId;                  // they sign in for the first time
        h.Db.SaveChanges();
        await reconciler.ReconcileAsync(StormSteamId, default);

        Assert.True((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }

    [Fact]
    public async Task ReconcilingRepeatedly_IsIdempotent()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;
        h.Db.SaveChanges();

        var reconciler = Build(h, new ComplimentaryGrant { SteamId = StormSteamId, Tier = "clan" });
        await reconciler.ReconcileAllAsync(default);
        await reconciler.ReconcileAllAsync(default);
        await reconciler.ReconcileAllAsync(default);

        // One row, not three — the unique index on UserId would reject the extras anyway.
        Assert.Equal(1, h.Db.Subscriptions.Count(s => s.UserId == h.User.Id));
    }

    [Fact]
    public async Task PaidSubscription_IsNeverOverwrittenByAGrant()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;
        h.AddPaidSubscription(PlanCatalog.Raider);
        h.Db.SaveChanges();

        await Build(h, new ComplimentaryGrant { SteamId = StormSteamId, Tier = "clan" }).ReconcileAllAsync(default);

        h.Db.ChangeTracker.Clear();
        var sub = await h.Service.GetSubscriptionAsync(h.User.Id, default);
        Assert.False(sub!.IsComplimentary);
        Assert.Equal(PlanCatalog.Raider, sub.PlanTier);
    }

    [Fact]
    public async Task UnknownTier_IsIgnoredRatherThanThrowing()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;
        h.Db.SaveChanges();

        await Build(h, new ComplimentaryGrant { SteamId = StormSteamId, Tier = "Clan" }) // wrong case
            .ReconcileAllAsync(default);

        // PlanCatalog.IsKnownTier is case-insensitive, so this one actually does apply — which is
        // the point: an operator typo in casing should not silently cost someone their access.
        Assert.True((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }

    /// <summary>Several grants are configured as a list, and each has to land on its own account.
    /// Worth pinning because a loop that reused one lookup would quietly grant only the first.</summary>
    [Fact]
    public async Task MultipleGrants_EachApplyToTheirOwnAccount()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;

        var second = new User { Username = "Second", SteamId = "76561199373420447" };
        h.Db.Users.Add(second);
        h.Db.SaveChanges();

        await Build(h,
            new ComplimentaryGrant { SteamId = StormSteamId, Tier = "clan", Reason = "storm account" },
            new ComplimentaryGrant { SteamId = "76561199373420447", Tier = "clan", Reason = "granted access" })
            .ReconcileAllAsync(default);

        foreach (var userId in new[] { h.User.Id, second.Id })
        {
            var entitlement = await h.Service.GetEntitlementAsync(userId, default);
            Assert.True(entitlement.IsEntitled);
            Assert.Equal(PlanCatalog.Clan, entitlement.Tier);
            Assert.True(entitlement.IsComplimentary);
        }

        Assert.Equal(2, h.Db.Subscriptions.Count());
    }

    /// <summary>A second grant must not disturb one already applied — the reconciler runs on every
    /// login, so this happens constantly in normal operation.</summary>
    [Fact]
    public async Task AddingAGrant_LeavesExistingOnesUntouched()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;
        var second = new User { Username = "Second", SteamId = "76561199373420447" };
        h.Db.Users.Add(second);
        h.Db.SaveChanges();

        await Build(h, new ComplimentaryGrant { SteamId = StormSteamId, Tier = "clan", Reason = "storm account" })
            .ReconcileAllAsync(default);

        await Build(h,
            new ComplimentaryGrant { SteamId = StormSteamId, Tier = "clan", Reason = "storm account" },
            new ComplimentaryGrant { SteamId = "76561199373420447", Tier = "clan", Reason = "granted access" })
            .ReconcileAllAsync(default);

        h.Db.ChangeTracker.Clear();
        var storm = await h.Service.GetSubscriptionAsync(h.User.Id, default);
        Assert.Equal("storm account", storm!.CompReason);
        Assert.Equal(2, h.Db.Subscriptions.Count());
    }

    [Fact]
    public async Task GenuinelyUnknownTier_GrantsNothing()
    {
        using var h = new BillingTestHarness();
        h.User.SteamId = StormSteamId;
        h.Db.SaveChanges();

        await Build(h, new ComplimentaryGrant { SteamId = StormSteamId, Tier = "platinum" })
            .ReconcileAllAsync(default);

        Assert.False((await h.Service.GetEntitlementAsync(h.User.Id, default)).IsEntitled);
    }
}
