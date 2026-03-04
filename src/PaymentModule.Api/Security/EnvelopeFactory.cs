// Mirrors the EnvelopeFactory used in the Main Server SDK (Papermaker.PaymentSDKNEW.Core.Messaging)
namespace Papermaker.PaymentSDKNEW.Core.Messaging;

/// <summary>
/// Creates signed response envelopes that are identical in shape to what the Main Server expects.
/// Headers bag contains: X-Correlation-ID, X-Timestamp, and optionally Idempotency-Key.
/// </summary>
public class EnvelopeFactory
{
    /// <summary>
    /// Wraps a payload in a MessageEnvelope.
    /// - IsSuccess indicates if the operation succeeded (default: true).
    /// </summary>
    public MessageEnvelope<T> Create<T>(T payload, string? correlationId = null, string? idempotencyKey = null, bool isSuccess = true)
    {
        return new MessageEnvelope<T>
        {
            Payload = payload,
            IsSuccess = isSuccess
        };
    }
}
