using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rustex.Domain.Billing;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Infrastructure.Billing;

/// <summary>Accounts granted a plan without paying, configured by SteamID.
///
/// Replaces the static site's old <c>COMPED_ACCOUNTS</c> environment variable. Same idea — an
/// operator-controlled list — but it now materialises into a real <c>Subscription</c> row, so the
/// grant is visible, auditable and revocable rather than being re-derived from an env var on
/// every request.</summary>
public class ComplimentaryGrantOptions
{
    public const string SectionName = "Billing";

    public List<ComplimentaryGrant> ComplimentaryGrants { get; set; } = [];
}

public class ComplimentaryGrant
{
    public string SteamId { get; set; } = default!;
    public string Tier { get; set; } = default!;
    public string? Reason { get; set; }
}

/// <summary>Applies configured grants to the matching account.
///
/// Called from two places because a grant can be configured before or after the person first
/// signs in: once at startup for accounts that already exist, and again on each Steam login for
/// accounts that did not exist yet. Both paths are idempotent.</summary>
public interface IComplimentaryGrantReconciler
{
    Task ReconcileAsync(string steamId, CancellationToken ct);
    Task ReconcileAllAsync(CancellationToken ct);
}

public sealed class ComplimentaryGrantReconciler : IComplimentaryGrantReconciler
{
    private readonly AppDbContext _db;
    private readonly ISubscriptionService _subscriptions;
    private readonly ComplimentaryGrantOptions _options;
    private readonly ILogger<ComplimentaryGrantReconciler> _log;

    public ComplimentaryGrantReconciler(
        AppDbContext db,
        ISubscriptionService subscriptions,
        IOptions<ComplimentaryGrantOptions> options,
        ILogger<ComplimentaryGrantReconciler> log)
    {
        _db = db;
        _subscriptions = subscriptions;
        _options = options.Value;
        _log = log;
    }

    public async Task ReconcileAsync(string steamId, CancellationToken ct)
    {
        var grant = _options.ComplimentaryGrants.FirstOrDefault(g =>
            string.Equals(g.SteamId?.Trim(), steamId, StringComparison.Ordinal));
        if (grant is null) return;

        if (!PlanCatalog.IsKnownTier(grant.Tier))
        {
            _log.LogError("Complimentary grant for {SteamId} names unknown tier '{Tier}' — ignoring", steamId, grant.Tier);
            return;
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.SteamId == steamId, ct);
        if (user is null)
        {
            // Normal before their first sign-in: the login path calls us again once the account
            // exists, so there is nothing to fix here.
            _log.LogDebug("Complimentary grant for {SteamId} has no account yet", steamId);
            return;
        }

        var existing = await _subscriptions.GetSubscriptionAsync(user.Id, ct);

        // Already granted at this tier — nothing to do. Re-granting would be harmless but would
        // log noise on every login.
        if (existing is not null && existing.IsComplimentary
            && string.Equals(existing.PlanTier, grant.Tier, StringComparison.OrdinalIgnoreCase))
            return;

        // Never override something the person is actually paying for.
        if (existing is not null && !existing.IsComplimentary && existing.IsEntitled)
        {
            _log.LogWarning("Skipping complimentary grant for {SteamId}: they have a paid subscription", steamId);
            return;
        }

        await _subscriptions.GrantComplimentaryAsync(
            user.Id, grant.Tier, grant.Reason ?? "Configured complimentary access", ct);

        _log.LogInformation("Applied complimentary {Tier} to SteamID {SteamId}", grant.Tier, steamId);
    }

    public async Task ReconcileAllAsync(CancellationToken ct)
    {
        foreach (var grant in _options.ComplimentaryGrants)
        {
            if (string.IsNullOrWhiteSpace(grant.SteamId)) continue;
            try
            {
                await ReconcileAsync(grant.SteamId.Trim(), ct);
            }
            catch (Exception ex)
            {
                // One bad entry must not stop the rest, and must never stop the app booting.
                _log.LogError(ex, "Failed to apply complimentary grant for {SteamId}", grant.SteamId);
            }
        }
    }
}

/// <summary>Applies configured grants once at startup, for accounts that already exist.</summary>
public sealed class ComplimentaryGrantStartupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ComplimentaryGrantStartupWorker> _log;

    public ComplimentaryGrantStartupWorker(IServiceScopeFactory scopeFactory, ILogger<ComplimentaryGrantStartupWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reconciler = scope.ServiceProvider.GetRequiredService<IComplimentaryGrantReconciler>();
            await reconciler.ReconcileAllAsync(ct);
        }
        catch (Exception ex)
        {
            // Billing grants are not worth refusing to serve traffic over.
            _log.LogError(ex, "Complimentary grant reconciliation failed at startup");
        }
    }
}
