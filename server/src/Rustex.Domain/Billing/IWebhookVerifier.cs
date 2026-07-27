namespace Rustex.Domain.Billing;

/// <summary>A provider webhook, already proven authentic and flattened into our own vocabulary.
/// Only the fields we act on are carried — anything else in the payload is deliberately dropped
/// rather than passed through to a handler that might trust it.</summary>
public sealed record ProviderWebhookEvent(
    string EventId,
    string EventType,
    DateTimeOffset Created,
    string? SubscriptionId,
    string? CustomerId,
    ProviderInvoice? Invoice,
    ProviderPaymentMethod? PaymentMethod);

/// <summary>Proves a webhook really came from the payment provider.
///
/// This is the entire security boundary for the webhook endpoint: it cannot require a login,
/// because the provider has no session with us, so an unsigned request must be indistinguishable
/// from an attacker's. <see cref="Verify"/> must therefore be the first thing any handler calls,
/// and it must be given the <b>raw</b> request body — re-serialising the JSON changes the bytes
/// and would invalidate a legitimate signature.</summary>
public interface IWebhookVerifier
{
    /// <summary>False when no signing secret is configured. Handlers must reject everything in
    /// that state rather than processing unverified events.</summary>
    bool IsConfigured { get; }

    /// <summary>Throws <see cref="PaymentProviderException"/> if the signature does not match, is
    /// missing, or the timestamp is outside the replay window.</summary>
    ProviderWebhookEvent Verify(string rawBody, string? signatureHeader);
}
