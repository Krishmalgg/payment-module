namespace PaymentModule.Domain.Entities;

/// <summary>
/// Unified failure log for all communication types.
/// Stores messages/requests that exhausted all retry attempts.
///
/// CommunicationType values:
///   "RabbitMq" — incoming RabbitMQ consumer failed all retries (also NACKed to broker DLQ)
///   "Http"     — outgoing HTTP S2S dispatch failed all retries (no broker to NACK)
/// </summary>
public class FailedMessage
{
    public Guid Id { get; private set; }

    /// <summary>Identifies which consumer/command type failed, e.g. "PaymentIntent", "Refund".</summary>
    public string MessageType { get; private set; } = null!;

    /// <summary>The queue name (RabbitMQ) or endpoint URL (HTTP) the message originated from.</summary>
    public string Source { get; private set; } = null!;

    /// <summary>How the message was being communicated when it failed: "RabbitMq" or "Http".</summary>
    public string CommunicationType { get; private set; } = null!;

    /// <summary>Raw JSON payload of the original message.</summary>
    public string Payload { get; private set; } = null!;

    public string FailureReason { get; private set; } = null!;
    public int RetryCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastAttemptAt { get; private set; }
    public bool Resolved { get; private set; }

    public FailedMessage(
        Guid id,
        string messageType,
        string source,
        string communicationType,
        string payload,
        string failureReason,
        int retryCount)
    {
        Id = id;
        MessageType = messageType;
        Source = source;
        CommunicationType = communicationType;
        Payload = payload;
        FailureReason = failureReason;
        RetryCount = retryCount;
        Resolved = false;
        CreatedAt = DateTimeOffset.UtcNow;
        LastAttemptAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsResolved() => Resolved = true;

    // Private constructor for EF Core
    private FailedMessage() { }
}
