using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rustex.Domain.Billing;

namespace Rustex.Api.Controllers;

/// <summary>Receives subscription lifecycle events from the payment provider.
///
/// This is the only endpoint in the API that is both unauthenticated and state-changing, because
/// the provider has no session with us. Its security rests entirely on the signature check in
/// <see cref="IWebhookVerifier"/>: the raw body is HMAC'd with a secret only we and the provider
/// know, so an attacker who finds this URL can still not make it do anything.
///
/// Three rules this handler follows, all of which matter:
///  1. Verify before parsing — never act on unverified JSON.
///  2. Claim the event id first — providers retry, and a replayed <c>invoice.paid</c> must not
///     write a second invoice row.
///  3. Return 2xx once handled. A non-2xx makes the provider retry, so genuine handling failures
///     are surfaced as 500 (retry me) while events we intentionally ignore return 200.</summary>
[ApiController]
[Route("api/billing/webhook")]
[AllowAnonymous]
public class BillingWebhookController : ControllerBase
{
    private const string SignatureHeader = "Stripe-Signature";

    private readonly IWebhookVerifier _verifier;
    private readonly ISubscriptionService _subscriptions;
    private readonly ILogger<BillingWebhookController> _log;

    public BillingWebhookController(
        IWebhookVerifier verifier, ISubscriptionService subscriptions, ILogger<BillingWebhookController> log)
    {
        _verifier = verifier;
        _subscriptions = subscriptions;
        _log = log;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!_verifier.IsConfigured)
        {
            // 503, not 200: the provider should keep retrying until the secret is configured,
            // rather than treating unprocessable events as delivered and dropping them.
            _log.LogError("Webhook received but no signing secret is configured — refusing to process.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // The signature covers the exact bytes sent. Model binding or re-serialising the JSON
        // would change them and break verification, so the body is read raw.
        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync(ct);

        ProviderWebhookEvent evt;
        try
        {
            evt = _verifier.Verify(body, Request.Headers[SignatureHeader]);
        }
        catch (PaymentProviderException ex)
        {
            _log.LogWarning("Rejected webhook: {Message}", ex.Message);
            return BadRequest();
        }

        if (!await _subscriptions.TryClaimWebhookEventAsync(evt.EventId, evt.EventType, ct))
            return Ok(new { status = "duplicate" });

        try
        {
            await HandleAsync(evt, ct);
        }
        catch (Exception ex)
        {
            // Surface as 500 so the provider retries. The claim above has already been committed,
            // so log loudly: a retry will be treated as a duplicate and skipped.
            _log.LogError(ex, "Failed handling webhook {EventId} ({EventType})", evt.EventId, evt.EventType);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok(new { status = "handled" });
    }

    private async Task HandleAsync(ProviderWebhookEvent evt, CancellationToken ct)
    {
        switch (evt.EventType)
        {
            // Checkout finished, or the subscription changed anywhere (including directly in the
            // provider's dashboard). All of these resolve to "re-read the truth and store it".
            case "checkout.session.completed":
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
            case "customer.subscription.paused":
            case "customer.subscription.resumed":
                if (evt.SubscriptionId is not null)
                    await _subscriptions.SyncFromProviderAsync(evt.SubscriptionId, evt.Created, ct);
                break;

            case "invoice.paid":
            case "invoice.payment_failed":
            case "invoice.finalized":
            case "invoice.voided":
                if (evt.Invoice is not null)
                    await _subscriptions.RecordInvoiceAsync(evt.Invoice, ct);

                // A failed payment moves the subscription to past_due, and a successful one moves
                // it back to active — neither is reported by the invoice event itself.
                if (evt.SubscriptionId is not null)
                    await _subscriptions.SyncFromProviderAsync(evt.SubscriptionId, evt.Created, ct);
                break;

            case "payment_method.attached":
            case "payment_method.updated":
                if (evt.PaymentMethod is not null && evt.CustomerId is not null)
                    await _subscriptions.UpsertPaymentMethodAsync(evt.CustomerId, evt.PaymentMethod, ct);
                break;

            default:
                // Endpoints are usually subscribed to more event types than they act on. Ignoring
                // the rest with a 200 stops the provider retrying something we will never handle.
                _log.LogDebug("Ignoring webhook event type {EventType}", evt.EventType);
                break;
        }
    }
}
