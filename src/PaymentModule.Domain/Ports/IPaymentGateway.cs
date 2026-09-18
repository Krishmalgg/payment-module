using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Domain.Ports;

/// <summary>
/// Provider-neutral payment gateway contract.
///
/// Implementations translate between this vocabulary and one provider's API. No
/// provider-specific term, status code or field name may appear in this interface
/// or in the types it returns — that is the whole point of the port.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Canonical lowercase provider key — "payhere", "paypal", "stripe", "mock".
    /// The adapter names itself so registration and routing never hardcode a list.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Starts a payment. When <paramref name="paymentMethodToken"/> is supplied the
    /// adapter may charge the saved card immediately instead of returning a checkout action.
    /// </summary>
    Task<PaymentIntentResult> CreatePaymentIntent(
        TransactionId id,
        Money amount,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct,
        string? paymentMethodToken = null);

    /// <summary>
    /// Starts a card tokenisation flow (PayHere preapproval / Stripe SetupIntent / PayPal vault).
    /// </summary>
    Task<CardSetupResult> InitiateCardSetup(
        string orderId,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct);

    /// <summary>
    /// Verifies and parses a provider notification into neutral terms.
    /// Signature verification MUST happen inside the adapter.
    /// </summary>
    Task<WebhookResult> HandleWebhook(
        string payload,
        IDictionary<string, string> headers,
        CancellationToken ct);

    /// <summary>
    /// Refunds a captured payment. A null <paramref name="amount"/> means a full refund.
    /// </summary>
    Task<RefundResult> RefundAsync(
        string providerRefId,
        decimal? amount,
        string currency,
        string description,
        CancellationToken ct);
}
