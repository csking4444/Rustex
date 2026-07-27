namespace Rustex.Domain.Entities;

/// <summary>Where a user's entitlement came from. Kept explicit because a complimentary grant
/// must never be rendered as a paid subscription — no invoices, no renewal date, no "cancel"
/// button. See <see cref="Subscription.IsComplimentary"/>.</summary>
public enum SubscriptionSource
{
    Stripe,
    Complimentary,
}

/// <summary>Mirrors Stripe's subscription status vocabulary. We store what Stripe tells us
/// rather than deriving our own, so the two can never disagree about whether someone is paid up.
/// <c>Incomplete</c> means checkout started but the first payment has not settled — deliberately
/// NOT an entitling status.</summary>
public enum SubscriptionStatus
{
    Incomplete,
    Trialing,
    Active,
    PastDue,
    Canceled,
    Unpaid,
    Paused,
}

public enum BillingInterval
{
    Monthly,
    Yearly,
}

public enum InvoiceStatus
{
    Draft,
    Open,
    Paid,
    Uncollectible,
    Void,
}

/// <summary>One row per user. The provider is the source of truth for status and period dates —
/// every field below that mirrors Stripe is written by the webhook handler, never by a request
/// handler acting on what the browser claimed.</summary>
public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    /// <summary>Tier key from <c>PlanCatalog</c> ("scout" / "raider" / "clan").</summary>
    public string PlanTier { get; set; } = default!;
    public BillingInterval Interval { get; set; } = BillingInterval.Monthly;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Incomplete;
    public SubscriptionSource Source { get; set; } = SubscriptionSource.Stripe;

    /// <summary>A grant made by us rather than bought. Nothing is ever billed against it, so
    /// billing history, renewal dates and payment methods stay genuinely empty.</summary>
    public bool IsComplimentary => Source == SubscriptionSource.Complimentary;
    public string? CompReason { get; set; }

    public string? ProviderCustomerId { get; set; }
    public string? ProviderSubscriptionId { get; set; }

    public DateTimeOffset? CurrentPeriodStart { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }

    /// <summary>Set when the user cancels: they keep access until <see cref="CurrentPeriodEnd"/>,
    /// which is what they already paid for.</summary>
    public bool CancelAtPeriodEnd { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>Guards against out-of-order webhook delivery — Stripe does not promise ordering,
    /// so an older event must never overwrite newer state.</summary>
    public DateTimeOffset? ProviderUpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    /// <summary>Does this subscription currently unlock paid features? Trialing and PastDue
    /// both still entitle — Stripe keeps retrying a past-due card, and locking someone out
    /// mid-retry loses customers who would have paid. Unpaid/Canceled do not.</summary>
    public bool IsEntitled =>
        Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue;
}

/// <summary>A billing document mirrored from the provider. We never generate invoice numbers or
/// amounts ourselves — inventing those would be fabricating financial records.</summary>
public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    public string ProviderInvoiceId { get; set; } = default!;
    /// <summary>Human-facing number as issued by the provider (e.g. "B1F4A2C3-0001").</summary>
    public string? Number { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public long AmountDueCents { get; set; }
    public long AmountPaidCents { get; set; }
    public string Currency { get; set; } = "usd";

    /// <summary>Provider-hosted links. We deliberately do not render our own PDF — the provider's
    /// document is the legal record.</summary>
    public string? HostedInvoiceUrl { get; set; }
    public string? InvoicePdfUrl { get; set; }

    public DateTimeOffset? PeriodStart { get; set; }
    public DateTimeOffset? PeriodEnd { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Display-only card details. This is the *entire* payment instrument we are allowed to
/// hold: brand, last four, expiry. The card number, CVC and full expiry never touch our servers —
/// they go from the user's browser straight to Stripe via Checkout/Elements, and we only ever see
/// an opaque payment-method id. That is what keeps this system out of PCI scope.</summary>
public class PaymentMethodSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public string ProviderPaymentMethodId { get; set; } = default!;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Webhook idempotency ledger. Stripe retries on any non-2xx and can deliver the same
/// event more than once even on success, so every handler checks here first — otherwise a retry
/// of <c>invoice.paid</c> would duplicate an invoice row.</summary>
public class ProcessedWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProviderEventId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
