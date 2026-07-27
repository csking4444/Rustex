namespace Rustex.Infrastructure.Billing;

/// <summary>Stripe wiring. Every value here is a secret or an account-specific id, so all of it
/// comes from configuration — nothing is defaulted to a working value, because a silently-wrong
/// default in billing means charging the wrong amount to the wrong account.</summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Secret API key (<c>sk_test_…</c> / <c>sk_live_…</c>). Server-side only — this must
    /// never be sent to a browser. The publishable key is not needed at all: we use hosted
    /// Checkout, so the client never initialises Stripe.js itself.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Signing secret for the webhook endpoint (<c>whsec_…</c>). Without it we cannot
    /// tell a real Stripe delivery from anyone who found the URL, so the webhook refuses to
    /// process anything when this is unset.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Stripe Price ids per tier and interval, e.g. <c>Stripe:Prices:scout:Monthly</c>.
    /// Outer key is the plan tier, inner key is "Monthly"/"Yearly".</summary>
    public Dictionary<string, PlanPriceIds> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where Checkout sends the browser back to. Absolute URL of the frontend.</summary>
    public string? CheckoutSuccessPath { get; set; } = "/billing?checkout=success";
    public string? CheckoutCancelPath { get; set; } = "/billing?checkout=cancelled";
    public string? PortalReturnPath { get; set; } = "/billing";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
    public bool WebhooksConfigured => !string.IsNullOrWhiteSpace(WebhookSecret);
}

public class PlanPriceIds
{
    public string? Monthly { get; set; }
    public string? Yearly { get; set; }
}
