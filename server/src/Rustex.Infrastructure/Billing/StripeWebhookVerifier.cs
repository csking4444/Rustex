using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rustex.Domain.Billing;
using Rustex.Domain.Entities;
using Stripe;

namespace Rustex.Infrastructure.Billing;

/// <summary>Verifies Stripe's <c>Stripe-Signature</c> header and flattens the event.
///
/// <c>EventUtility.ConstructEvent</c> does the security-relevant work: it recomputes the HMAC over
/// the raw payload with our signing secret and rejects timestamps outside a tolerance window, so a
/// captured-and-replayed delivery is refused as well as a forged one.</summary>
public sealed class StripeWebhookVerifier : IWebhookVerifier
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripeWebhookVerifier> _log;

    public StripeWebhookVerifier(IOptions<StripeOptions> options, ILogger<StripeWebhookVerifier> log)
    {
        _options = options.Value;
        _log = log;
    }

    public bool IsConfigured => _options.WebhooksConfigured;

    public ProviderWebhookEvent Verify(string rawBody, string? signatureHeader)
    {
        if (!IsConfigured)
            throw new PaymentProviderException("Webhook signing secret is not configured.");
        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new PaymentProviderException("Missing signature header.");

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                rawBody, signatureHeader, _options.WebhookSecret,
                // throwOnApiVersionMismatch: false — our account's API version can be rolled
                // forward in the Stripe dashboard independently of this SDK, and refusing those
                // events would silently stop all billing updates.
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _log.LogWarning("Rejected webhook with invalid signature: {Message}", ex.Message);
            throw new PaymentProviderException("Invalid webhook signature.", ex);
        }

        return Flatten(stripeEvent);
    }

    private static ProviderWebhookEvent Flatten(Event e)
    {
        string? subscriptionId = null;
        string? customerId = null;
        ProviderInvoice? invoice = null;
        ProviderPaymentMethod? paymentMethod = null;

        switch (e.Data.Object)
        {
            case Stripe.Subscription sub:
                subscriptionId = sub.Id;
                customerId = sub.CustomerId;
                break;

            case Stripe.Checkout.Session session:
                subscriptionId = session.SubscriptionId;
                customerId = session.CustomerId;
                break;

            case Stripe.Invoice inv:
                customerId = inv.CustomerId;
                subscriptionId = inv.Parent?.SubscriptionDetails?.SubscriptionId;
                invoice = new ProviderInvoice(
                    inv.Id, inv.Number, MapInvoiceStatus(inv.Status), inv.AmountDue, inv.AmountPaid,
                    inv.Currency ?? "usd", inv.HostedInvoiceUrl, inv.InvoicePdf,
                    ToOffset(inv.PeriodStart), ToOffset(inv.PeriodEnd),
                    ToOffset(inv.StatusTransitions?.FinalizedAt ?? inv.Created),
                    ToOffset(inv.StatusTransitions?.PaidAt), subscriptionId);
                break;

            case Stripe.PaymentMethod pm:
                customerId = pm.CustomerId;
                paymentMethod = new ProviderPaymentMethod(
                    pm.Id, pm.Card?.Brand, pm.Card?.Last4, (int?)pm.Card?.ExpMonth, (int?)pm.Card?.ExpYear);
                break;
        }

        return new ProviderWebhookEvent(
            e.Id, e.Type, ToOffset(e.Created) ?? DateTimeOffset.UtcNow,
            subscriptionId, customerId, invoice, paymentMethod);
    }

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
}
