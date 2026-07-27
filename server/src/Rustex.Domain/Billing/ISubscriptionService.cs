using Rustex.Domain.Entities;

namespace Rustex.Domain.Billing;

/// <summary>What a user is allowed to do right now. This is the only shape the API layer should
/// consult when gating a feature — it already folds together paid subscriptions, complimentary
/// grants and the plan catalog, so no caller has to reimplement that precedence.</summary>
public sealed record Entitlement(
    bool IsEntitled,
    string? Tier,
    string? PlanName,
    SubscriptionStatus? Status,
    bool IsComplimentary,
    BillingInterval? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    int ServerLimit,
    int TeamMemberLimit,
    IReadOnlySet<string> Features)
{
    /// <summary>Nobody signed in without a plan. Deliberately a real value rather than null so
    /// callers cannot forget a null check and accidentally treat "no plan" as "allowed".</summary>
    public static readonly Entitlement None = new(
        false, null, null, null, false, null, null, false, 0, 0, new HashSet<string>());

    public bool Allows(string feature) => IsEntitled && Features.Contains(feature);
}

/// <summary>Thrown for a request that is well-formed but not valid for this subscription's current
/// state — downgrading to the tier you are already on, resuming something already ended. Maps to
/// 409, not 500.</summary>
public sealed class SubscriptionStateException : Exception
{
    public SubscriptionStateException(string message) : base(message) { }
}

public interface ISubscriptionService
{
    /// <summary>Resolved, cached entitlement for a user. Safe to call on every gated request.</summary>
    Task<Entitlement> GetEntitlementAsync(Guid userId, CancellationToken ct);

    Task<Subscription?> GetSubscriptionAsync(Guid userId, CancellationToken ct);

    /// <summary>Hosted checkout URL for a new subscription.</summary>
    Task<string> StartCheckoutAsync(Guid userId, string tier, BillingInterval interval, CancellationToken ct);

    /// <summary>Moves an existing subscription between tiers/intervals, choosing proration based on
    /// whether it is an upgrade or a downgrade.</summary>
    Task<Subscription> ChangePlanAsync(Guid userId, string tier, BillingInterval interval, CancellationToken ct);

    Task<Subscription> CancelAsync(Guid userId, bool immediately, CancellationToken ct);
    Task<Subscription> ResumeAsync(Guid userId, CancellationToken ct);

    /// <summary>Provider-hosted page for changing the card on file. Returns the URL to redirect to.</summary>
    Task<string> CreatePaymentMethodUpdateUrlAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<Invoice>> GetBillingHistoryAsync(Guid userId, int limit, CancellationToken ct);
    Task<PaymentMethodSummary?> GetPaymentMethodAsync(Guid userId, CancellationToken ct);

    /// <summary>Pulls current state from the provider and writes it locally. Used by the webhook
    /// and as a self-heal path when local state looks stale.</summary>
    Task<Subscription?> SyncFromProviderAsync(string providerSubscriptionId, DateTimeOffset eventTime, CancellationToken ct);

    Task RecordInvoiceAsync(ProviderInvoice invoice, CancellationToken ct);

    /// <summary>Stores the display-only card details for whoever owns this provider customer.</summary>
    Task UpsertPaymentMethodAsync(string customerId, ProviderPaymentMethod method, CancellationToken ct);

    /// <summary>Claims a webhook event id, returning false if it has already been handled. The
    /// provider retries on any non-2xx and may deliver twice even on success, so every handler
    /// must gate on this or risk duplicating financial records.</summary>
    Task<bool> TryClaimWebhookEventAsync(string eventId, string eventType, CancellationToken ct);

    /// <summary>Grants access without any payment. Replaces the old env-var allowlist with a real,
    /// audited row so it can be listed, revoked and reasoned about.</summary>
    Task<Subscription> GrantComplimentaryAsync(Guid userId, string tier, string? reason, CancellationToken ct);
    Task RevokeComplimentaryAsync(Guid userId, CancellationToken ct);

    Task InvalidateEntitlementCacheAsync(Guid userId);
}
