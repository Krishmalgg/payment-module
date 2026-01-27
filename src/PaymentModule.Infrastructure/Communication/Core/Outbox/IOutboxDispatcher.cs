using PaymentModule.Domain.Entities;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;

namespace PaymentModule.Infrastructure.Communication.Core.Outbox;

/// <summary>
/// Interface for dispatching outbox messages to their respective handlers.
/// </summary>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Dispatches the message to a handler based on its type.
    /// Used for both immediate (Hybrid) send and background service retries.
    /// </summary>
    Task<ProducerResult> DispatchAsync(OutboxMessage message, CancellationToken cancellationToken);
}
