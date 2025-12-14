using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Domain.Ports;

public interface IPaymentGateway
{
    Task<object> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct);
}

