using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rustex.Domain.Billing;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Billing;
using Xunit;

namespace Rustex.Api.Tests.Billing;

/// <summary>A deployment with no Stripe keys must still run.
///
/// These exist because the opposite was true and only showed up when the API was actually
/// started: <c>StripeClient</c> throws on an empty key, so building it in the constructor made
/// <see cref="IPaymentProvider"/> impossible to resolve — which took down every endpoint that
/// merely had it somewhere in its dependency graph, including the plan catalog, which never
/// touches Stripe at all.</summary>
public class UnconfiguredBillingTests
{
    private static StripePaymentProvider Provider(string? secretKey) =>
        new(Options.Create(new StripeOptions { SecretKey = secretKey }),
            NullLogger<StripePaymentProvider>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructing_WithoutAKey_DoesNotThrow(string? key)
    {
        // Resolution must succeed; only actually calling Stripe may fail.
        var provider = Provider(key);
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task CallingStripe_WithoutAKey_FailsAsAPaymentError()
    {
        var provider = Provider(null);

        // PaymentProviderException maps to a 4xx with a readable message, rather than surfacing
        // as an ArgumentException the exception middleware would report as a bare "bad_request".
        var ex = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            provider.EnsureCustomerAsync(Guid.NewGuid(), "a@b.test", "tester", default));
        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanCatalog_NeedsNoProviderConfiguration()
    {
        // The pricing page must render before sign-in and before billing is wired up.
        Assert.Equal(3, PlanCatalog.All.Count);
        Assert.Equal(499, PlanCatalog.Find(PlanCatalog.Scout)!.MonthlyCents);
        Assert.Equal(999, PlanCatalog.Find(PlanCatalog.Raider)!.MonthlyCents);
        Assert.Equal(1999, PlanCatalog.Find(PlanCatalog.Clan)!.MonthlyCents);
    }

    [Fact]
    public void UnconfiguredOptions_ReportNotPurchasable()
    {
        var options = new StripeOptions();
        Assert.False(options.IsConfigured);
        Assert.False(options.WebhooksConfigured);
    }

    [Fact]
    public async Task Checkout_WithoutConfiguration_IsRejectedWithAReadableReason()
    {
        using var h = new BillingTestHarness();

        // Same shape the controller surfaces: a 409 the UI can explain, not a 500.
        var unconfigured = new SubscriptionService(
            h.Db, Provider(null), h.Cache,
            Options.Create(new StripeOptions()),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<SubscriptionService>.Instance);

        var ex = await Assert.ThrowsAsync<SubscriptionStateException>(() =>
            unconfigured.StartCheckoutAsync(h.User.Id, PlanCatalog.Clan, BillingInterval.Monthly, default));
        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
