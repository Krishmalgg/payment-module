using Microsoft.Extensions.Logging;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Infrastructure.Gateways.Adapters;

/// <summary>
/// In-memory gateway used for local development and automated tests.
///
/// Deliberately exercises a different <see cref="CheckoutAction"/> than PayHere
/// (Redirect rather than FormPost) so that client code which hardcodes one shape
/// fails loudly here rather than silently in production against a second provider.
///
/// Webhook payloads are simple query strings, e.g.
///   order_id=ORD-1&amp;status=completed&amp;amount=100.00&amp;currency=LKR
/// </summary>
public class MockAdapter : IPaymentGateway
{
    private readonly ILogger<MockAdapter> _logger;

    public string Provider => "mock";

    public MockAdapter(ILogger<MockAdapter> logger)
    {
        _logger = logger;
    }

    private static PaymentStatus MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "completed" or "success" => PaymentStatus.Completed,
        "pending" => PaymentStatus.Pending,
        "cancelled" or "canceled" => PaymentStatus.Cancelled,
        "chargeback" => PaymentStatus.Chargeback,
        "failed" => PaymentStatus.Failed,
        _ => PaymentStatus.Unknown
    };

    public Task<PaymentIntentResult> CreatePaymentIntent(
        TransactionId id,
        Money amount,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct,
        string? paymentMethodToken = null)
    {
        var meta = metadata ?? new Dictionary<string, string>();
        var orderId = meta.GetValueOrDefault("order_id", id.Value.ToString());

        _logger.LogInformation("[Mock] Creating payment intent for Order {OrderId} ({Amount} {Currency})",
            orderId, amount.Value, amount.Currency);

        // Saved-card charge settles immediately in the mock.
        if (!string.IsNullOrEmpty(paymentMethodToken))
        {
            return Task.FromResult(new PaymentIntentResult(
                Provider: Provider,
                Action: CheckoutAction.None,
                Status: PaymentStatus.Completed,
                Url: null,
                Fields: new Dictionary<string, string> { ["order_id"] = orderId },
                ProviderReference: $"MOCK-PAY-{Guid.NewGuid():N}"[..20]));
        }

        return Task.FromResult(new PaymentIntentResult(
            Provider: Provider,
            Action: CheckoutAction.Redirect,
            Status: PaymentStatus.Pending,
            Url: $"https://mock-gateway.local/checkout/{orderId}",
            Fields: new Dictionary<string, string>
            {
                ["order_id"] = orderId,
                ["amount"] = amount.Value.ToString("0.00"),
                ["currency"] = amount.Currency
            }));
    }

    public Task<CardSetupResult> InitiateCardSetup(
        string orderId,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
    {
        _logger.LogInformation("[Mock] Initiating card setup for Order {OrderId}", orderId);

        return Task.FromResult(new CardSetupResult(
            Provider: Provider,
            Action: CheckoutAction.Redirect,
            Url: $"https://mock-gateway.local/add-card/{orderId}",
            Fields: new Dictionary<string, string> { ["order_id"] = orderId }));
    }

    public Task<WebhookResult> HandleWebhook(
        string payload,
        IDictionary<string, string> headers,
        CancellationToken ct)
    {
        var form = ParseForm(payload);

        if (!form.TryGetValue("order_id", out var orderId) || string.IsNullOrEmpty(orderId))
        {
            return Task.FromResult(WebhookResult.Invalid("", "Missing order_id"));
        }

        var rawStatus = form.GetValueOrDefault("status", "completed");
        var token = form.GetValueOrDefault("customer_token", "");

        var card = string.IsNullOrEmpty(token)
            ? null
            : new CardDetails(token, "Mock Cardholder", "************4242", "12/30", "VISA");

        decimal? amount = decimal.TryParse(form.GetValueOrDefault("amount"), out var a) ? a : null;

        return Task.FromResult(new WebhookResult(
            SignatureValid: true,
            EventType: card is null ? WebhookEventType.Payment : WebhookEventType.CardSetup,
            Status: MapStatus(rawStatus),
            OrderId: orderId,
            ProviderReference: form.GetValueOrDefault("payment_id", $"MOCK-{orderId}"),
            Amount: amount,
            Currency: form.GetValueOrDefault("currency", "LKR"),
            Card: card,
            Metadata: new Dictionary<string, string>(form),
            RawStatus: rawStatus));
    }

    public Task<RefundResult> RefundAsync(
        string providerRefId,
        decimal? amount,
        string currency,
        string description,
        CancellationToken ct)
    {
        _logger.LogInformation("[Mock] Refunding {Ref} for {Amount} {Currency}",
            providerRefId, amount?.ToString() ?? "FULL", currency);

        return Task.FromResult(new RefundResult(
            IsSuccess: true,
            Status: "COMPLETED",
            ProviderRefundId: $"MOCK-RFND-{Guid.NewGuid():N}"[..22],
            ErrorMessage: null));
    }

    private static Dictionary<string, string> ParseForm(string payload)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(payload)) return dict;

        foreach (var p in payload.Split('&'))
        {
            var kv = p.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0] ?? "");
            var val = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
            if (!string.IsNullOrEmpty(key)) dict[key] = val;
        }
        return dict;
    }
}
