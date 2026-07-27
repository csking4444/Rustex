using Rustex.Domain.Entities;

namespace Rustex.Domain.Billing;

/// <summary>Provider-neutral snapshot of a subscription. Deliberately not Stripe's type: the
/// domain and the API layer must never take a dependency on the payment SDK, so swapping
/// providers later is an infrastructure change only.</summary>
public sealed record ProviderSubscription(
    string SubscriptionId,
    string CustomerId,
    string PriceId,
    SubscriptionStatus Status,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt);

public sealed record ProviderInvoice(
    string InvoiceId,
    string? Number,
    InvoiceStatus Status,
    long AmountDueCents,
    long AmountPaidCents,
    string Currency,
    string? HostedInvoiceUrl,
    string? InvoicePdfUrl,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? PaidAt,
    string? SubscriptionId);

/// <summary>Only ever the safe-to-display fields. There is no shape here that could carry a card
/// number, by design.</summary>
public sealed record ProviderPaymentMethod(
    string PaymentMethodId,
    string? Brand,
    string? Last4,
    int? ExpMonth,
    int? ExpYear);

public sealed record CheckoutSession(string SessionId, string Url);

/// <summary>Raised for anything the provider rejects that the user can act on (declined card,
/// no such subscription). Mapped to a 4xx rather than a 500 so the UI can say something useful.</summary>
public sealed class PaymentProviderException : Exception
{
    public PaymentProviderException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The seam between our billing logic and whoever actually moves the money.
///
/// Note what is absent: nothing here accepts a card number, CVC or bank detail. Card data goes
/// from the browser directly to the provider through their hosted Checkout, and we only ever
/// handle opaque ids. Any future implementation must preserve that property.</summary>
public interface IPaymentProvider
{
    /// <summary>Idempotent: returns the existing provider customer for this user, creating one
    /// only if absent.</summary>
    Task<string> EnsureCustomerAsync(Guid userId, string? email, string? username, CancellationToken ct);

    Task<CheckoutSession> CreateCheckoutSessionAsync(
        Guid userId, string customerId, string priceId, string successUrl, string cancelUrl, CancellationToken ct);

    /// <summary>Hosted page where the customer can change their card or download past invoices.
    /// Using the provider's own portal means card entry never passes through our origin.</summary>
    Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl, CancellationToken ct);

    /// <summary>Switches the subscription to a different price. <paramref name="prorate"/> should
    /// be true for an upgrade (charge the difference now) and false for a downgrade (let the
    /// cheaper tier begin next period, since the customer already paid for this one).</summary>
    Task<ProviderSubscription> ChangePlanAsync(
        string subscriptionId, string newPriceId, bool prorate, CancellationToken ct);

    Task<ProviderSubscription> CancelAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct);

    /// <summary>Undoes a pending end-of-period cancellation. Only valid while the subscription is
    /// still within a paid period.</summary>
    Task<ProviderSubscription> ResumeAsync(string subscriptionId, CancellationToken ct);

    Task<ProviderSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct);

    Task<IReadOnlyList<ProviderInvoice>> ListInvoicesAsync(string customerId, int limit, CancellationToken ct);

    Task<ProviderPaymentMethod?> GetDefaultPaymentMethodAsync(string customerId, CancellationToken ct);
}
