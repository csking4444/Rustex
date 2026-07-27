using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rustex.Domain.Billing;
using Rustex.Domain.Entities;
using Stripe;
// Only Checkout is imported: Stripe.BillingPortal has a SessionService/SessionCreateOptions of the
// same name, so portal types stay fully qualified below rather than making every Session* ambiguous.
using Stripe.Checkout;

namespace Rustex.Infrastructure.Billing;

/// <summary>Stripe implementation of <see cref="IPaymentProvider"/>.
///
/// Everything Stripe-shaped stops at this file: callers get the provider-neutral records from
/// <c>IPaymentProvider</c>, so no controller or domain service ever holds a <c>Stripe.*</c> type.
///
/// Note that no method here accepts card details. Card entry happens on Stripe's own hosted
/// Checkout and Billing Portal pages, which means a raw PAN never reaches our servers or logs.</summary>
public sealed class StripePaymentProvider : IPaymentProvider
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentProvider> _log;
    private readonly Lazy<Services> _services;

    /// <summary>The Stripe service objects, built together from one client.</summary>
    private sealed record Services(
        CustomerService Customers,
        // Fully qualified: our own SubscriptionService lives in this namespace and would otherwise win.
        Stripe.SubscriptionService Subscriptions,
        InvoiceService Invoices,
        PaymentMethodService PaymentMethods,
        SessionService CheckoutSessions,
        Stripe.BillingPortal.SessionService PortalSessions);

    public StripePaymentProvider(IOptions<StripeOptions> options, ILogger<StripePaymentProvider> log)
    {
        _options = options.Value;
        _log = log;

        // Built on first use, not here. StripeClient throws on an empty key, so constructing it
        // eagerly made this type impossible to resolve on a deployment without billing keys —
        // which took down even endpoints that never touch Stripe, like the plan catalog.
        _services = new Lazy<Services>(() =>
        {
            if (!_options.IsConfigured)
                throw new PaymentProviderException("Billing is not configured on this deployment.");

            // A per-instance client rather than the global StripeConfiguration.ApiKey: static
            // global state is awkward to test and would leak between any future multi-tenant setup.
            var client = new StripeClient(_options.SecretKey);
            return new Services(
                new CustomerService(client),
                new Stripe.SubscriptionService(client),
                new InvoiceService(client),
                new PaymentMethodService(client),
                new SessionService(client),
                new Stripe.BillingPortal.SessionService(client));
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private CustomerService _customers => _services.Value.Customers;
    private Stripe.SubscriptionService _subscriptions => _services.Value.Subscriptions;
    private InvoiceService _invoices => _services.Value.Invoices;
    private PaymentMethodService _paymentMethods => _services.Value.PaymentMethods;
    private SessionService _checkoutSessions => _services.Value.CheckoutSessions;
    private Stripe.BillingPortal.SessionService _portalSessions => _services.Value.PortalSessions;

    public async Task<string> EnsureCustomerAsync(Guid userId, string? email, string? username, CancellationToken ct)
    {
        // Look the customer up by our own id before creating one. Without this, a user who
        // abandons checkout and retries would accumulate duplicate Stripe customers, and the
        // "which customer owns this subscription?" question stops having one answer.
        var existing = await Guard(() => _customers.SearchAsync(
            new CustomerSearchOptions { Query = $"metadata['rustex_user_id']:'{userId}'", Limit = 1 },
            cancellationToken: ct));

        if (existing.Data.Count > 0) return existing.Data[0].Id;

        var created = await Guard(() => _customers.CreateAsync(new CustomerCreateOptions
        {
            Email = email,
            Name = username,
            Metadata = new Dictionary<string, string> { ["rustex_user_id"] = userId.ToString() },
        }, cancellationToken: ct));

        _log.LogInformation("Created Stripe customer {CustomerId} for user {UserId}", created.Id, userId);
        return created.Id;
    }

    public async Task<CheckoutSession> CreateCheckoutSessionAsync(
        Guid userId, string customerId, string priceId, string successUrl, string cancelUrl, CancellationToken ct)
    {
        var session = await Guard(() => _checkoutSessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = customerId,
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            // Stamped on both the session and the resulting subscription so the webhook can
            // attribute it to a user even if our local row was somehow lost.
            ClientReferenceId = userId.ToString(),
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { ["rustex_user_id"] = userId.ToString() },
            },
        }, cancellationToken: ct));

        return new CheckoutSession(session.Id, session.Url);
    }

    public async Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl, CancellationToken ct)
    {
        var session = await Guard(() => _portalSessions.CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl },
            cancellationToken: ct));
        return session.Url;
    }

    public async Task<ProviderSubscription> ChangePlanAsync(
        string subscriptionId, string newPriceId, bool prorate, CancellationToken ct)
    {
        var current = await Guard(() => _subscriptions.GetAsync(subscriptionId, cancellationToken: ct));
        var item = current.Items?.Data?.FirstOrDefault()
            ?? throw new PaymentProviderException("Subscription has no billable item to change.");

        if (string.Equals(item.Price?.Id, newPriceId, StringComparison.Ordinal))
            throw new PaymentProviderException("Subscription is already on that plan.");

        var updated = await Guard(() => _subscriptions.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
        {
            Items = [new SubscriptionItemOptions { Id = item.Id, Price = newPriceId }],
            // Upgrades bill the difference straight away; downgrades take effect at renewal so we
            // are not refunding time the customer has already been served.
            ProrationBehavior = prorate ? "always_invoice" : "none",
            // A pending cancellation would silently survive a plan change and cut access off at
            // period end — clearing it makes the change mean what the user expects.
            CancelAtPeriodEnd = false,
        }, cancellationToken: ct));

        return Map(updated);
    }

    public async Task<ProviderSubscription> CancelAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct)
    {
        if (atPeriodEnd)
        {
            var updated = await Guard(() => _subscriptions.UpdateAsync(subscriptionId,
                new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: ct));
            return Map(updated);
        }

        var cancelled = await Guard(() => _subscriptions.CancelAsync(subscriptionId, null, cancellationToken: ct));
        return Map(cancelled);
    }

    public async Task<ProviderSubscription> ResumeAsync(string subscriptionId, CancellationToken ct)
    {
        var current = await Guard(() => _subscriptions.GetAsync(subscriptionId, cancellationToken: ct));
        if (current.Status == "canceled")
            throw new PaymentProviderException("This subscription has already ended and cannot be resumed. Start a new one instead.");

        var updated = await Guard(() => _subscriptions.UpdateAsync(subscriptionId,
            new SubscriptionUpdateOptions { CancelAtPeriodEnd = false }, cancellationToken: ct));
        return Map(updated);
    }

    public async Task<ProviderSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        try
        {
            var sub = await _subscriptions.GetAsync(subscriptionId, cancellationToken: ct);
            return Map(sub);
        }
        catch (StripeException ex) when (ex.StripeError?.Type == "invalid_request_error")
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ProviderInvoice>> ListInvoicesAsync(string customerId, int limit, CancellationToken ct)
    {
        var list = await Guard(() => _invoices.ListAsync(
            new InvoiceListOptions { Customer = customerId, Limit = Math.Clamp(limit, 1, 100) },
            cancellationToken: ct));
        return list.Data.Select(Map).ToList();
    }

    public async Task<ProviderPaymentMethod?> GetDefaultPaymentMethodAsync(string customerId, CancellationToken ct)
    {
        var customer = await Guard(() => _customers.GetAsync(customerId,
            new CustomerGetOptions { Expand = ["invoice_settings.default_payment_method"] },
            cancellationToken: ct));

        var pm = customer.InvoiceSettings?.DefaultPaymentMethod;
        if (pm is null)
        {
            // No explicit default: fall back to the most recently attached card, which is what
            // Stripe would charge anyway.
            var cards = await Guard(() => _paymentMethods.ListAsync(
                new PaymentMethodListOptions { Customer = customerId, Type = "card", Limit = 1 },
                cancellationToken: ct));
            pm = cards.Data.FirstOrDefault();
        }

        return pm is null ? null : Map(pm);
    }

    // ---------- mapping ----------

    private static ProviderSubscription Map(Stripe.Subscription s)
    {
        // Since API version 2025-03, the billing period lives on the subscription *item*, not the
        // subscription — Subscription.CurrentPeriodStart/End no longer exist on this SDK version.
        var item = s.Items?.Data?.FirstOrDefault();

        return new ProviderSubscription(
            SubscriptionId: s.Id,
            CustomerId: s.CustomerId,
            PriceId: item?.Price?.Id ?? string.Empty,
            Status: MapStatus(s.Status),
            CurrentPeriodStart: ToOffset(item?.CurrentPeriodStart),
            CurrentPeriodEnd: ToOffset(item?.CurrentPeriodEnd),
            TrialEndsAt: ToOffset(s.TrialEnd),
            CancelAtPeriodEnd: s.CancelAtPeriodEnd,
            CanceledAt: ToOffset(s.CanceledAt));
    }

    private static ProviderInvoice Map(Stripe.Invoice i) => new(
        InvoiceId: i.Id,
        Number: i.Number,
        Status: MapInvoiceStatus(i.Status),
        AmountDueCents: i.AmountDue,
        AmountPaidCents: i.AmountPaid,
        Currency: i.Currency ?? "usd",
        HostedInvoiceUrl: i.HostedInvoiceUrl,
        InvoicePdfUrl: i.InvoicePdf,
        PeriodStart: ToOffset(i.PeriodStart),
        PeriodEnd: ToOffset(i.PeriodEnd),
        IssuedAt: ToOffset(i.StatusTransitions?.FinalizedAt ?? i.Created),
        PaidAt: ToOffset(i.StatusTransitions?.PaidAt),
        // The subscription link also moved: it is under parent.subscription_details now.
        SubscriptionId: i.Parent?.SubscriptionDetails?.SubscriptionId);

    private static ProviderPaymentMethod Map(Stripe.PaymentMethod pm) => new(
        PaymentMethodId: pm.Id,
        Brand: pm.Card?.Brand,
        Last4: pm.Card?.Last4,
        ExpMonth: (int?)pm.Card?.ExpMonth,
        ExpYear: (int?)pm.Card?.ExpYear);

    /// <summary>Unknown statuses map to <see cref="SubscriptionStatus.Incomplete"/>, which does not
    /// entitle. If Stripe adds a status we do not know, failing closed loses a little revenue;
    /// failing open gives away the product.</summary>
    private static SubscriptionStatus MapStatus(string? status) => status switch
    {
        "active" => SubscriptionStatus.Active,
        "trialing" => SubscriptionStatus.Trialing,
        "past_due" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" => SubscriptionStatus.Canceled,
        "unpaid" => SubscriptionStatus.Unpaid,
        "paused" => SubscriptionStatus.Paused,
        "incomplete" => SubscriptionStatus.Incomplete,
        _ => SubscriptionStatus.Incomplete,
    };

    private static InvoiceStatus MapInvoiceStatus(string? status) => status switch
    {
        "paid" => InvoiceStatus.Paid,
        "open" => InvoiceStatus.Open,
        "uncollectible" => InvoiceStatus.Uncollectible,
        "void" => InvoiceStatus.Void,
        _ => InvoiceStatus.Draft,
    };

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    /// <summary>Turns Stripe's exceptions into something a caller can map to a 4xx. A declined card
    /// or a bad price id is the user's problem to fix, not a server fault, and should not page us.</summary>
    private async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (StripeException ex)
        {
            _log.LogWarning(ex, "Stripe rejected a request: {Type}/{Code}", ex.StripeError?.Type, ex.StripeError?.Code);
            throw new PaymentProviderException(ex.StripeError?.Message ?? "The payment provider rejected that request.", ex);
        }
    }
}
