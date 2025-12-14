using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Infrastructure.Gateways.Adapters;

public class MockAdapter : IPaymentGateway
{
    public Task<object> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct)
    {
        var result = new
        {
            transactionId = id.Value,
            amount = amount.Value,
            currency = amount.Currency,
            status = "requires_confirmation",
            metadata
        };
        return Task.FromResult<object>(result);
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

