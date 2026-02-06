using PaymentModule.Infrastructure.Communication.Core.Abstractions;

namespace PaymentModule.Infrastructure.Communication.Core.Outbox;

/// <summary>
/// Defines a handler for a specific outbox message type.
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>
    /// The message type this handler supports (e.g., "PaymentStatus").
    /// </summary>
    string MessageType { get; }

    /// <summary>
    /// Handles the specific payload for the message type.
    /// </summary>
    Task<ProducerResult> HandleAsync(string payload, CancellationToken cancellationToken);
}
