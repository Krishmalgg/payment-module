using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Infrastructure.Gateways.Adapters;

public class MockAdapter : IPaymentGateway
{
    public Task<PaymentIntentResult> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct)
    {
        var result = new PaymentIntentResult(
            Gateway: "Mock",
            Action: "redirect",
            Url: $"https://mock-gateway.com/pay/{id.Value}",
            Fields: new Dictionary<string, string> 
            { 
                { "transactionId", id.Value.ToString() },
                { "amount", amount.Value.ToString() },
                { "currency", amount.Currency }
            }
        );
        return Task.FromResult(result);
    }

    public Task<object> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct)
    {
        var result = new
        {
            ok = true,
            received = payload?.Length ?? 0,
            headersCount = headers.Count
        };
        return Task.FromResult<object>(result);
    }
}

