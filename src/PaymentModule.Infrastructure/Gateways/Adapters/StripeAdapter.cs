// using PaymentModule.Domain.Ports;
// using PaymentModule.Domain.ValueObjects;

// namespace PaymentModule.Infrastructure.Gateways.Adapters;

// public class StripeAdapter : IPaymentGateway
// {
//     public Task<object> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct)
//     {
//         var result = new
//         {
//             gateway = "stripe",
//             configured = false,
//             reason = "missing_api_keys"
//         };
//         return Task.FromResult<object>(result);
//     }

//     public Task<object> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct)
//     {
//         var result = new
//         {
//             ok = false,
//             configured = false
//         };
//         return Task.FromResult<object>(result);
//     }
// }

