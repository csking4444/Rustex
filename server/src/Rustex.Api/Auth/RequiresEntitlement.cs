using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Rustex.Domain.Billing;

namespace Rustex.Api.Auth;

/// <summary>Status codes this gate uses, kept distinct on purpose so the frontend can tell the
/// three failure modes apart without parsing a message:
/// <list type="bullet">
/// <item>401 — not signed in (produced by the auth middleware, before this filter runs).</item>
/// <item>402 — signed in, but has no active plan. The UI should offer checkout.</item>
/// <item>403 — signed in and subscribed, but this feature needs a higher tier. The UI should
/// offer an upgrade, not a fresh checkout.</item>
/// </list></summary>
public static class EntitlementStatus
{
    public const int SubscriptionRequired = StatusCodes.Status402PaymentRequired;
    public const int UpgradeRequired = StatusCodes.Status403Forbidden;
}

/// <summary>Requires any active plan. Use on endpoints that are part of the paid product but not
/// tied to one specific feature.
///
/// This deliberately re-resolves entitlement server-side on every request rather than reading a
/// claim from the JWT: a token minted before someone cancelled would otherwise keep working until
/// it expired, which is exactly the window a paying-then-refunding user would exploit.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresSubscriptionAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider services) =>
        new EntitlementFilter(services.GetRequiredService<ISubscriptionService>(), feature: null);
}

/// <summary>Requires an active plan whose tier includes a specific capability. Feature keys come
/// from <see cref="Rustex.Domain.Billing.Features"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresFeatureAttribute : Attribute, IFilterFactory
{
    private readonly string _feature;

    public RequiresFeatureAttribute(string feature) => _feature = feature;

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider services) =>
        new EntitlementFilter(services.GetRequiredService<ISubscriptionService>(), _feature);
}

internal sealed class EntitlementFilter : IAsyncAuthorizationFilter
{
    private readonly ISubscriptionService _subscriptions;
    private readonly string? _feature;

    public EntitlementFilter(ISubscriptionService subscriptions, string? feature)
    {
        _subscriptions = subscriptions;
        _feature = feature;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var raw = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.HttpContext.User.FindFirstValue("sub");

        if (!Guid.TryParse(raw, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var entitlement = await _subscriptions.GetEntitlementAsync(userId, context.HttpContext.RequestAborted);

        if (!entitlement.IsEntitled)
        {
            context.Result = new ObjectResult(new
            {
                error = "subscription_required",
                message = "This feature needs an active Rustex plan.",
                requiredFeature = _feature,
            })
            { StatusCode = EntitlementStatus.SubscriptionRequired };
            return;
        }

        if (_feature is not null && !entitlement.Allows(_feature))
        {
            context.Result = new ObjectResult(new
            {
                error = "upgrade_required",
                message = "Your current plan does not include this feature.",
                requiredFeature = _feature,
                currentTier = entitlement.Tier,
            })
            { StatusCode = EntitlementStatus.UpgradeRequired };
        }
    }
}
