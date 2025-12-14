using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Infrastructure.Gateways.Adapters;

public class StripeAdapter : IPaymentGateway
{
    public Task<PaymentIntentResult> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct)
    {
        var result = new PaymentIntentResult(
            Gateway: "stripe",
            Action: "error", 
            Url: "",
            Fields: new Dictionary<string, string> { { "error", "Not configured" } }
        );
        return Task.FromResult(result);
    }

    public Task<object> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct)
    {
        var result = new
        {
            ok = false,
            configured = false
        };
        return Task.FromResult<object>(result);
    }
}

