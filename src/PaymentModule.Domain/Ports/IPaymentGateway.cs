using PaymentModule.Domain.ValueObjects;
using System.Collections.Generic;

namespace PaymentModule.Domain.Ports;

public interface IPaymentGateway
{
    Task<PaymentIntentResult> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct);
    Task<object> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct);
}
