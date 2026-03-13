namespace PaymentModule.Infrastructure.Communication.Core.Abstractions;

public interface IMessageProducer
{
    /// <summary>
    /// Sends a message to the specified destination.
    /// </summary>
    /// <param name="destination">For RabbitMQ: Queue Name. For HTTP: Target URL.</param>
    /// <param name="payload">The JSON payload.</param>
    /// <param name="ct">Cancellation Token.</param>
    /// <returns>ProducerResult containing success status and optional error message.</returns>
    Task<ProducerResult> SendAsync(
        string destination,
        string payload,
        CancellationToken ct = default,
        string? correlationId = null,
        IDictionary<string, object?>? headers = null);
}
