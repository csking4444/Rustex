using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rustex.Domain.Billing;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Caching;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Infrastructure.Billing;

/// <summary>Owns everything about who is subscribed to what.
///
/// The rule that keeps this honest: <b>the provider is the source of truth for money, and the
/// webhook is the only path that writes paid state.</b> Request handlers ask the provider to make
/// a change and then re-read it; they never assume the change took, and they never take the
/// client's word for a plan.</summary>
public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan EntitlementTtl = TimeSpan.FromSeconds(60);

    private readonly AppDbContext _db;
    private readonly IPaymentProvider _provider;
    private readonly IRedisCacheService _cache;
    private readonly StripeOptions _options;
    private readonly ILogger<SubscriptionService> _log;
    private readonly string _frontendUrl;

    public SubscriptionService(
        AppDbContext db,
        IPaymentProvider provider,
        IRedisCacheService cache,
        IOptions<StripeOptions> options,
        IConfiguration config,
        ILogger<SubscriptionService> log)
    {
        _db = db;
        _provider = provider;
        _cache = cache;
        _options = options.Value;
        _log = log;
        _frontendUrl = (config["App:FrontendUrl"] ?? config["Cors:AllowedOrigins:0"] ?? "http://localhost:5173")
            .TrimEnd('/');
    }

    // ---------- entitlement ----------

    private static string CacheKey(Guid userId) => $"entitlement:{userId}";

    public async Task<Entitlement> GetEntitlementAsync(Guid userId, CancellationToken ct)
    {
        var cached = await _cache.GetAsync<CachedEntitlement>(CacheKey(userId));
        if (cached is not null) return cached.ToEntitlement();

        var sub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var entitlement = Resolve(sub);

        await _cache.SetAsync(CacheKey(userId), CachedEntitlement.From(entitlement), EntitlementTtl);
        return entitlement;
    }

    /// <summary>Pure function from stored row to entitlement — no I/O, so it is trivially testable
    /// and the precedence rules live in exactly one place.</summary>
    private static Entitlement Resolve(Subscription? sub)
    {
        if (sub is null || !sub.IsEntitled) return Entitlement.None;

        var plan = PlanCatalog.Find(sub.PlanTier);
        // A row pointing at a tier we no longer sell must not silently grant the highest plan.
        if (plan is null) return Entitlement.None;

        return new Entitlement(
            IsEntitled: true,
            Tier: plan.Tier,
            PlanName: plan.Name,
            Status: sub.Status,
            IsComplimentary: sub.IsComplimentary,
            Interval: sub.IsComplimentary ? null : sub.Interval,
            CurrentPeriodEnd: sub.IsComplimentary ? null : sub.CurrentPeriodEnd,
            CancelAtPeriodEnd: sub.CancelAtPeriodEnd,
            ServerLimit: plan.ServerLimit,
            TeamMemberLimit: plan.TeamMemberLimit,
            Features: plan.FeatureKeys);
    }

    public Task InvalidateEntitlementCacheAsync(Guid userId) => _cache.RemoveAsync(CacheKey(userId));

    public Task<Subscription?> GetSubscriptionAsync(Guid userId, CancellationToken ct) =>
        _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);

    // ---------- purchase / change ----------

    public async Task<string> StartCheckoutAsync(Guid userId, string tier, BillingInterval interval, CancellationToken ct)
    {
        RequireConfigured();
        var priceId = ResolvePriceId(tier, interval);

        var existing = await _db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (existing is not null && existing.IsComplimentary)
            throw new SubscriptionStateException("This account already has complimentary access, so there is nothing to buy.");
        if (existing is not null && existing.IsEntitled)
            throw new SubscriptionStateException("You already have an active subscription. Change your plan instead of starting a new one.");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new SubscriptionStateException("Account not found.");

        var customerId = existing?.ProviderCustomerId
            ?? await _provider.EnsureCustomerAsync(userId, user.Email, user.Username, ct);

        var session = await _provider.CreateCheckoutSessionAsync(
            userId, customerId, priceId,
            $"{_frontendUrl}{_options.CheckoutSuccessPath}",
            $"{_frontendUrl}{_options.CheckoutCancelPath}",
            ct);

        // Remember the customer id now so a user who abandons checkout and comes back does not
        // get a second Stripe customer created for them.
        if (existing is null)
        {
            _db.Subscriptions.Add(new Subscription
            {
                UserId = userId,
                PlanTier = tier,
                Interval = interval,
                Status = SubscriptionStatus.Incomplete,
                Source = SubscriptionSource.Stripe,
                ProviderCustomerId = customerId,
            });
        }
        else
        {
            existing.ProviderCustomerId = customerId;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await InvalidateEntitlementCacheAsync(userId);
        return session.Url;
    }

    public async Task<Subscription> ChangePlanAsync(Guid userId, string tier, BillingInterval interval, CancellationToken ct)
    {
        RequireConfigured();
        var newPriceId = ResolvePriceId(tier, interval);
        var sub = await RequireSubscription(userId, ct);

        if (sub.IsComplimentary)
            throw new SubscriptionStateException("Complimentary access cannot be changed from here.");
        if (string.IsNullOrEmpty(sub.ProviderSubscriptionId))
            throw new SubscriptionStateException("There is no active subscription to change. Start one from the plans page.");
        if (string.Equals(sub.PlanTier, tier, StringComparison.OrdinalIgnoreCase) && sub.Interval == interval)
            throw new SubscriptionStateException("You are already on that plan.");

        // Upgrades charge the difference immediately; downgrades take effect at the next renewal,
        // so the customer keeps the tier they already paid for until the period ends.
        var isUpgrade = PlanCatalog.RankOf(tier) > PlanCatalog.RankOf(sub.PlanTier)
            || (PlanCatalog.RankOf(tier) == PlanCatalog.RankOf(sub.PlanTier)
                && interval == BillingInterval.Yearly && sub.Interval == BillingInterval.Monthly);

        var updated = await _provider.ChangePlanAsync(sub.ProviderSubscriptionId, newPriceId, isUpgrade, ct);

        ApplyProvider(sub, updated, tier, interval, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        await InvalidateEntitlementCacheAsync(userId);

        _log.LogInformation("User {UserId} {Direction} to {Tier}/{Interval}",
            userId, isUpgrade ? "upgraded" : "downgraded", tier, interval);
        return sub;
    }

    public async Task<Subscription> CancelAsync(Guid userId, bool immediately, CancellationToken ct)
    {
        var sub = await RequireSubscription(userId, ct);

        if (sub.IsComplimentary)
            throw new SubscriptionStateException("Complimentary access has no subscription to cancel.");
        if (string.IsNullOrEmpty(sub.ProviderSubscriptionId))
            throw new SubscriptionStateException("There is no active subscription to cancel.");
        if (sub.Status == SubscriptionStatus.Canceled)
            throw new SubscriptionStateException("This subscription has already been cancelled.");

        RequireConfigured();
        var updated = await _provider.CancelAsync(sub.ProviderSubscriptionId, !immediately, ct);

        ApplyProvider(sub, updated, sub.PlanTier, sub.Interval, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        await InvalidateEntitlementCacheAsync(userId);
        return sub;
    }

    public async Task<Subscription> ResumeAsync(Guid userId, CancellationToken ct)
    {
        var sub = await RequireSubscription(userId, ct);

        if (sub.IsComplimentary)
            throw new SubscriptionStateException("Complimentary access does not need resuming.");
        if (string.IsNullOrEmpty(sub.ProviderSubscriptionId))
            throw new SubscriptionStateException("There is no subscription to resume.");
        if (!sub.CancelAtPeriodEnd)
            throw new SubscriptionStateException("This subscription is not scheduled to cancel.");

        RequireConfigured();
        var updated = await _provider.ResumeAsync(sub.ProviderSubscriptionId, ct);

        ApplyProvider(sub, updated, sub.PlanTier, sub.Interval, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        await InvalidateEntitlementCacheAsync(userId);
        return sub;
    }

    public async Task<string> CreatePaymentMethodUpdateUrlAsync(Guid userId, CancellationToken ct)
    {
        RequireConfigured();
        var sub = await RequireSubscription(userId, ct);

        if (sub.IsComplimentary)
            throw new SubscriptionStateException("Complimentary access has no payment method to update.");
        if (string.IsNullOrEmpty(sub.ProviderCustomerId))
            throw new SubscriptionStateException("No billing account exists yet. Subscribe to a plan first.");

        // Deliberately the provider's hosted portal rather than our own card form: card details
        // then never traverse our origin, which is what keeps this out of PCI scope.
        return await _provider.CreateBillingPortalSessionAsync(
            sub.ProviderCustomerId, $"{_frontendUrl}{_options.PortalReturnPath}", ct);
    }

    // ---------- history ----------

    public async Task<IReadOnlyList<Invoice>> GetBillingHistoryAsync(Guid userId, int limit, CancellationToken ct)
    {
        var sub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);

        // Complimentary access is never billed, so there is genuinely nothing to show — we return
        // empty rather than inventing a $0 invoice history.
        if (sub is null || sub.IsComplimentary || string.IsNullOrEmpty(sub.ProviderCustomerId))
            return [];

        return await _db.Invoices.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IssuedAt ?? x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<PaymentMethodSummary?> GetPaymentMethodAsync(Guid userId, CancellationToken ct) =>
        await _db.PaymentMethods.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    // ---------- provider sync (webhook path) ----------

    public async Task<Subscription?> SyncFromProviderAsync(
        string providerSubscriptionId, DateTimeOffset eventTime, CancellationToken ct)
    {
        var remote = await _provider.GetSubscriptionAsync(providerSubscriptionId, ct);
        if (remote is null)
        {
            _log.LogWarning("Webhook referenced unknown subscription {SubscriptionId}", providerSubscriptionId);
            return null;
        }

        var sub = await _db.Subscriptions.FirstOrDefaultAsync(x => x.ProviderSubscriptionId == providerSubscriptionId, ct)
            ?? await _db.Subscriptions.FirstOrDefaultAsync(x => x.ProviderCustomerId == remote.CustomerId, ct);

        if (sub is null)
        {
            _log.LogWarning("No local subscription matches customer {CustomerId}; ignoring", remote.CustomerId);
            return null;
        }

        // Stripe does not guarantee delivery order. An event older than what we have already
        // applied must not roll state backwards.
        if (sub.ProviderUpdatedAt is not null && eventTime < sub.ProviderUpdatedAt)
        {
            _log.LogInformation("Skipping out-of-order event for {SubscriptionId}", providerSubscriptionId);
            return sub;
        }

        var (tier, interval) = ResolveTierFromPrice(remote.PriceId) ?? (sub.PlanTier, sub.Interval);
        ApplyProvider(sub, remote, tier, interval, eventTime);

        await _db.SaveChangesAsync(ct);
        await InvalidateEntitlementCacheAsync(sub.UserId);
        return sub;
    }

    public async Task RecordInvoiceAsync(ProviderInvoice invoice, CancellationToken ct)
    {
        var sub = invoice.SubscriptionId is null
            ? null
            : await _db.Subscriptions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProviderSubscriptionId == invoice.SubscriptionId, ct);

        if (sub is null)
        {
            _log.LogWarning("Invoice {InvoiceId} has no matching local subscription; skipping", invoice.InvoiceId);
            return;
        }

        var row = await _db.Invoices.FirstOrDefaultAsync(x => x.ProviderInvoiceId == invoice.InvoiceId, ct);
        if (row is null)
        {
            row = new Invoice { ProviderInvoiceId = invoice.InvoiceId, UserId = sub.UserId };
            _db.Invoices.Add(row);
        }

        row.SubscriptionId = sub.Id;
        row.Number = invoice.Number;
        row.Status = invoice.Status;
        row.AmountDueCents = invoice.AmountDueCents;
        row.AmountPaidCents = invoice.AmountPaidCents;
        row.Currency = invoice.Currency;
        row.HostedInvoiceUrl = invoice.HostedInvoiceUrl;
        row.InvoicePdfUrl = invoice.InvoicePdfUrl;
        row.PeriodStart = invoice.PeriodStart;
        row.PeriodEnd = invoice.PeriodEnd;
        row.IssuedAt = invoice.IssuedAt;
        row.PaidAt = invoice.PaidAt;

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpsertPaymentMethodAsync(string customerId, ProviderPaymentMethod method, CancellationToken ct)
    {
        var sub = await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProviderCustomerId == customerId, ct);
        if (sub is null)
        {
            _log.LogWarning("Payment method for unknown customer {CustomerId}; skipping", customerId);
            return;
        }

        var row = await _db.PaymentMethods
            .FirstOrDefaultAsync(x => x.ProviderPaymentMethodId == method.PaymentMethodId, ct);

        if (row is null)
        {
            row = new PaymentMethodSummary
            {
                UserId = sub.UserId,
                ProviderPaymentMethodId = method.PaymentMethodId,
            };
            _db.PaymentMethods.Add(row);

            // Newly attached card becomes the one we show. Demoting the others keeps a single
            // row flagged default, which is what GetPaymentMethodAsync orders on.
            var others = await _db.PaymentMethods.Where(x => x.UserId == sub.UserId).ToListAsync(ct);
            foreach (var other in others) other.IsDefault = false;
            row.IsDefault = true;
        }

        row.Brand = method.Brand;
        row.Last4 = method.Last4;
        row.ExpMonth = method.ExpMonth;
        row.ExpYear = method.ExpYear;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryClaimWebhookEventAsync(string eventId, string eventType, CancellationToken ct)
    {
        _db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
        {
            ProviderEventId = eventId,
            EventType = eventType,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Losing the unique-index race *is* the duplicate check — deciding this with a prior
            // read would leave a window where two concurrent deliveries both saw "not handled".
            _db.ChangeTracker.Clear();
            _log.LogInformation("Webhook {EventId} already handled; skipping", eventId);
            return false;
        }
    }

    // ---------- complimentary grants ----------

    public async Task<Subscription> GrantComplimentaryAsync(Guid userId, string tier, string? reason, CancellationToken ct)
    {
        if (!PlanCatalog.IsKnownTier(tier))
            throw new SubscriptionStateException($"Unknown plan tier '{tier}'.");

        var sub = await _db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (sub is not null && sub.Source == SubscriptionSource.Stripe && sub.IsEntitled)
            throw new SubscriptionStateException("This account has a paid subscription. Cancel it before granting complimentary access.");

        if (sub is null)
        {
            sub = new Subscription { UserId = userId };
            _db.Subscriptions.Add(sub);
        }

        sub.PlanTier = tier;
        sub.Source = SubscriptionSource.Complimentary;
        sub.Status = SubscriptionStatus.Active;
        sub.CompReason = reason;
        // A comp has no billing period, card or invoice — leaving these null is what stops the UI
        // rendering a renewal date that would never actually be charged.
        sub.CurrentPeriodStart = null;
        sub.CurrentPeriodEnd = null;
        sub.CancelAtPeriodEnd = false;
        sub.CanceledAt = null;
        sub.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        await InvalidateEntitlementCacheAsync(userId);

        _log.LogInformation("Granted complimentary {Tier} to user {UserId}: {Reason}", tier, userId, reason ?? "no reason given");
        return sub;
    }

    public async Task RevokeComplimentaryAsync(Guid userId, CancellationToken ct)
    {
        var sub = await _db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (sub is null || !sub.IsComplimentary) return;

        _db.Subscriptions.Remove(sub);
        await _db.SaveChangesAsync(ct);
        await InvalidateEntitlementCacheAsync(userId);
        _log.LogInformation("Revoked complimentary access for user {UserId}", userId);
    }

    // ---------- helpers ----------

    private void RequireConfigured()
    {
        if (!_options.IsConfigured)
            throw new SubscriptionStateException("Billing is not configured on this deployment.");
    }

    private async Task<Subscription> RequireSubscription(Guid userId, CancellationToken ct) =>
        await _db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, ct)
        ?? throw new SubscriptionStateException("You do not have a subscription yet.");

    private string ResolvePriceId(string tier, BillingInterval interval)
    {
        if (!PlanCatalog.IsKnownTier(tier))
            throw new SubscriptionStateException($"Unknown plan tier '{tier}'.");

        _options.Prices.TryGetValue(tier, out var ids);
        var priceId = interval == BillingInterval.Yearly ? ids?.Yearly : ids?.Monthly;

        if (string.IsNullOrWhiteSpace(priceId))
            throw new SubscriptionStateException(
                $"No price is configured for {tier}/{interval}. Set Stripe:Prices:{tier}:{interval}.");

        return priceId;
    }

    /// <summary>Reverse of <see cref="ResolvePriceId"/> — turns the price the provider reports back
    /// into our tier, so a plan change made in Stripe's dashboard still lands correctly here.</summary>
    private (string Tier, BillingInterval Interval)? ResolveTierFromPrice(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return null;

        foreach (var (tier, ids) in _options.Prices)
        {
            if (priceId == ids.Monthly) return (tier, BillingInterval.Monthly);
            if (priceId == ids.Yearly) return (tier, BillingInterval.Yearly);
        }
        return null;
    }

    private static void ApplyProvider(
        Subscription sub, ProviderSubscription remote, string tier, BillingInterval interval, DateTimeOffset eventTime)
    {
        sub.PlanTier = tier;
        sub.Interval = interval;
        sub.Status = remote.Status;
        sub.Source = SubscriptionSource.Stripe;
        sub.ProviderSubscriptionId = remote.SubscriptionId;
        sub.ProviderCustomerId = remote.CustomerId;
        sub.CurrentPeriodStart = remote.CurrentPeriodStart;
        sub.CurrentPeriodEnd = remote.CurrentPeriodEnd;
        sub.TrialEndsAt = remote.TrialEndsAt;
        sub.CancelAtPeriodEnd = remote.CancelAtPeriodEnd;
        sub.CanceledAt = remote.CanceledAt;
        sub.ProviderUpdatedAt = eventTime;
        sub.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Redis-friendly shape. <see cref="Entitlement"/> exposes <c>IReadOnlySet</c>, which
    /// System.Text.Json cannot construct on the way back in, so the cached form uses an array.</summary>
    private sealed record CachedEntitlement(
        bool IsEntitled, string? Tier, string? PlanName, SubscriptionStatus? Status, bool IsComplimentary,
        BillingInterval? Interval, DateTimeOffset? CurrentPeriodEnd, bool CancelAtPeriodEnd,
        int ServerLimit, int TeamMemberLimit, string[] Features)
    {
        public static CachedEntitlement From(Entitlement e) => new(
            e.IsEntitled, e.Tier, e.PlanName, e.Status, e.IsComplimentary, e.Interval,
            e.CurrentPeriodEnd, e.CancelAtPeriodEnd, e.ServerLimit, e.TeamMemberLimit, e.Features.ToArray());

        public Entitlement ToEntitlement() => new(
            IsEntitled, Tier, PlanName, Status, IsComplimentary, Interval, CurrentPeriodEnd,
            CancelAtPeriodEnd, ServerLimit, TeamMemberLimit, new HashSet<string>(Features));
    }
}
