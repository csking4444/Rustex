using System.ComponentModel.DataAnnotations;
using Rustex.Domain.Billing;
using Rustex.Domain.Entities;

namespace Rustex.Api.Dtos;

/// <summary>A purchasable tier as shown on the pricing page.</summary>
public record PlanResponse(
    string Tier,
    string Name,
    string Description,
    long MonthlyCents,
    long YearlyCents,
    int ServerLimit,
    int TeamMemberLimit,
    IReadOnlyList<string> Features,
    bool Purchasable)
{
    public static PlanResponse From(Plan p, bool purchasable) => new(
        p.Tier, p.Name, p.Description, p.MonthlyCents, p.YearlyCents,
        p.ServerLimit, p.TeamMemberLimit, p.FeatureKeys.OrderBy(f => f).ToList(), purchasable);
}

/// <summary>The caller's own subscription. Note the absence of any provider identifier — the
/// Stripe customer and subscription ids stay server-side, because a client that knows them gains
/// nothing legitimate and they are useful to an attacker probing our Stripe account.</summary>
public record SubscriptionResponse(
    bool HasSubscription,
    bool IsEntitled,
    string? Tier,
    string? PlanName,
    SubscriptionStatus? Status,
    BillingInterval? Interval,
    bool IsComplimentary,
    string? CompReason,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt,
    int ServerLimit,
    int TeamMemberLimit,
    IReadOnlyList<string> Features)
{
    public static SubscriptionResponse From(Subscription? sub, Entitlement ent) => new(
        HasSubscription: sub is not null,
        IsEntitled: ent.IsEntitled,
        Tier: sub?.PlanTier ?? ent.Tier,
        PlanName: ent.PlanName ?? PlanCatalog.Find(sub?.PlanTier)?.Name,
        Status: sub?.Status,
        // Complimentary access has no billing interval or renewal date. Reporting null rather
        // than a placeholder is what stops the UI inventing a charge that will never happen.
        Interval: sub is null || sub.IsComplimentary ? null : sub.Interval,
        IsComplimentary: sub?.IsComplimentary ?? false,
        CompReason: sub?.CompReason,
        CurrentPeriodEnd: sub is null || sub.IsComplimentary ? null : sub.CurrentPeriodEnd,
        TrialEndsAt: sub?.TrialEndsAt,
        CancelAtPeriodEnd: sub?.CancelAtPeriodEnd ?? false,
        CanceledAt: sub?.CanceledAt,
        ServerLimit: ent.ServerLimit,
        TeamMemberLimit: ent.TeamMemberLimit,
        Features: ent.Features.OrderBy(f => f).ToList());
}

public record InvoiceResponse(
    string Id,
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
    DateTimeOffset? PaidAt)
{
    public static InvoiceResponse From(Invoice i) => new(
        i.ProviderInvoiceId, i.Number, i.Status, i.AmountDueCents, i.AmountPaidCents,
        i.Currency, i.HostedInvoiceUrl, i.InvoicePdfUrl, i.PeriodStart, i.PeriodEnd, i.IssuedAt, i.PaidAt);
}

/// <summary>Card details safe to render. There is no field here that could hold a full card
/// number, and the API never returns one — see <see cref="PaymentMethodSummary"/>.</summary>
public record PaymentMethodResponse(string? Brand, string? Last4, int? ExpMonth, int? ExpYear)
{
    public static PaymentMethodResponse From(PaymentMethodSummary p) =>
        new(p.Brand, p.Last4, p.ExpMonth, p.ExpYear);
}

public class StartCheckoutRequest
{
    [Required, RegularExpression("^(scout|raider|clan)$", ErrorMessage = "Tier must be scout, raider or clan.")]
    public string Tier { get; set; } = default!;

    public BillingInterval Interval { get; set; } = BillingInterval.Monthly;
}

public class ChangePlanRequest
{
    [Required, RegularExpression("^(scout|raider|clan)$", ErrorMessage = "Tier must be scout, raider or clan.")]
    public string Tier { get; set; } = default!;

    public BillingInterval Interval { get; set; } = BillingInterval.Monthly;
}

public class CancelSubscriptionRequest
{
    /// <summary>Default false: cancelling at period end is what the user almost always means, and
    /// it does not throw away time they have already paid for.</summary>
    public bool Immediately { get; set; }
}

public record CheckoutSessionResponse(string Url);
public record PortalSessionResponse(string Url);
