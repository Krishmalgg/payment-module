using PaymentModule.Domain.ValueObjects;
using System.Collections.Generic;

namespace PaymentModule.Domain.Ports;

public interface IPaymentGateway
{
    Task<PaymentIntentResult> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct, string? customerToken = null);
    Task<PreapprovalResult> InitiatePreapproval(string orderId, Dictionary<string, string>? metadata, CancellationToken ct);
    Task<WebhookResult> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct);
}
