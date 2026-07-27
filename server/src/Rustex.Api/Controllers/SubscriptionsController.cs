using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Rustex.Api.Dtos;
using Rustex.Domain.Billing;
using Rustex.Infrastructure.Billing;

namespace Rustex.Api.Controllers;

/// <summary>The caller's own billing. Every action here operates on the subscription belonging to
/// the authenticated user — there is deliberately no route parameter for a user or subscription id,
/// so one account can never address another's billing by guessing an identifier.</summary>
[ApiController]
[Route("api/billing")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;
    private readonly StripeOptions _stripe;
    private readonly ILogger<SubscriptionsController> _log;

    public SubscriptionsController(
        ISubscriptionService subscriptions, IOptions<StripeOptions> stripe, ILogger<SubscriptionsController> log)
    {
        _subscriptions = subscriptions;
        _stripe = stripe.Value;
        _log = log;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    /// <summary>The plan catalog. Anonymous because the pricing page needs it before sign-in.
    /// <c>Purchasable</c> reports whether checkout is actually wired up on this deployment, so the
    /// UI can be honest instead of offering a button that cannot work.</summary>
    [HttpGet("plans")]
    [AllowAnonymous]
    public ActionResult<IEnumerable<PlanResponse>> GetPlans()
    {
        return Ok(PlanCatalog.All.Select(p =>
        {
            _stripe.Prices.TryGetValue(p.Tier, out var ids);
            var purchasable = _stripe.IsConfigured && !string.IsNullOrWhiteSpace(ids?.Monthly);
            return PlanResponse.From(p, purchasable);
        }));
    }

    [HttpGet("subscription")]
    public async Task<ActionResult<SubscriptionResponse>> GetSubscription(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var sub = await _subscriptions.GetSubscriptionAsync(userId, ct);
        var ent = await _subscriptions.GetEntitlementAsync(userId, ct);
        return Ok(SubscriptionResponse.From(sub, ent));
    }

    /// <summary>Resolved entitlement on its own — what the dashboard polls to decide which features
    /// to render. Cheap and cached; safe to call often.</summary>
    [HttpGet("entitlement")]
    public async Task<ActionResult<Entitlement>> GetEntitlement(CancellationToken ct) =>
        Ok(await _subscriptions.GetEntitlementAsync(CurrentUserId, ct));

    [HttpPost("checkout")]
    public async Task<ActionResult<CheckoutSessionResponse>> StartCheckout(
        [FromBody] StartCheckoutRequest request, CancellationToken ct)
    {
        var url = await _subscriptions.StartCheckoutAsync(CurrentUserId, request.Tier, request.Interval, ct);
        return Ok(new CheckoutSessionResponse(url));
    }

    [HttpPost("change-plan")]
    public async Task<ActionResult<SubscriptionResponse>> ChangePlan(
        [FromBody] ChangePlanRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var sub = await _subscriptions.ChangePlanAsync(userId, request.Tier, request.Interval, ct);
        var ent = await _subscriptions.GetEntitlementAsync(userId, ct);
        return Ok(SubscriptionResponse.From(sub, ent));
    }

    [HttpPost("cancel")]
    public async Task<ActionResult<SubscriptionResponse>> Cancel(
        [FromBody] CancelSubscriptionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var sub = await _subscriptions.CancelAsync(userId, request.Immediately, ct);
        var ent = await _subscriptions.GetEntitlementAsync(userId, ct);
        _log.LogInformation("User {UserId} cancelled their subscription (immediate={Immediate})", userId, request.Immediately);
        return Ok(SubscriptionResponse.From(sub, ent));
    }

    [HttpPost("resume")]
    public async Task<ActionResult<SubscriptionResponse>> Resume(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var sub = await _subscriptions.ResumeAsync(userId, ct);
        var ent = await _subscriptions.GetEntitlementAsync(userId, ct);
        return Ok(SubscriptionResponse.From(sub, ent));
    }

    /// <summary>Returns a URL to the provider's hosted billing portal. We never accept card details
    /// ourselves, so "update payment method" is necessarily a redirect rather than a form post.</summary>
    [HttpPost("payment-method")]
    public async Task<ActionResult<PortalSessionResponse>> UpdatePaymentMethod(CancellationToken ct)
    {
        var url = await _subscriptions.CreatePaymentMethodUpdateUrlAsync(CurrentUserId, ct);
        return Ok(new PortalSessionResponse(url));
    }

    [HttpGet("payment-method")]
    public async Task<ActionResult<PaymentMethodResponse?>> GetPaymentMethod(CancellationToken ct)
    {
        var pm = await _subscriptions.GetPaymentMethodAsync(CurrentUserId, ct);
        return Ok(pm is null ? null : PaymentMethodResponse.From(pm));
    }

    /// <summary>Past invoices, newest first. Genuinely empty for accounts that have never been
    /// charged — including complimentary ones, which are never billed at all.</summary>
    [HttpGet("invoices")]
    public async Task<ActionResult<IEnumerable<InvoiceResponse>>> GetInvoices(
        [FromQuery] int limit = 25, CancellationToken ct = default)
    {
        var invoices = await _subscriptions.GetBillingHistoryAsync(CurrentUserId, limit, ct);
        return Ok(invoices.Select(InvoiceResponse.From));
    }
}
