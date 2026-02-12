namespace PaymentModule.Domain.Entities;

/// <summary>
/// Stores failed refund attempts for manual resolution
/// </summary>
public class DeadLetterQueue
{
    public Guid Id { get; private set; }
    public Guid RefundId { get; private set; }
    public string Payload { get; private set; } = null!; // JSON string
    public string FailureReason { get; private set; } = null!;
    public int RetryCount { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime LastAttemptAt { get; private set; } = DateTime.UtcNow;
    public bool Resolved { get; private set; }

    public DeadLetterQueue(
        Guid id,
        Guid refundId,
        string payload,
        string failureReason,
        int retryCount)
    {
        Id = id;
        RefundId = refundId;
        Payload = payload;
        FailureReason = failureReason;
        RetryCount = retryCount;
        Resolved = false;
    }

    public void MarkAsResolved()
    {
        Resolved = true;
    }

    public void UpdateLastAttempt()
    {
        LastAttemptAt = DateTime.UtcNow;
    }

    // Private constructor for EF Core
    private DeadLetterQueue() { }
}
