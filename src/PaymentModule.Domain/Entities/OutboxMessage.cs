using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTime OccurredAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public string? ProcessedBy { get; private set; }  // Instance ID that processed this message
    public int RetryCount { get; private set; }
    public DateTime? LockedUntil { get; private set; }  // Lock expiration time for distributed processing

    public OutboxMessage(string type, string payload)
    {
        Id = Guid.NewGuid();
        Type = type;
        Payload = payload;
        OccurredAt = DateTime.UtcNow;
        RetryCount = 0;
    }

    public void MarkAsProcessed(string processInstanceId)
    {
        ProcessedAt = DateTime.UtcNow;
        ProcessedBy = processInstanceId;
    }

    public bool AcquireLock(string instanceId, int lockTimeoutSeconds)
    {
        if (LockedUntil == null || LockedUntil < DateTime.UtcNow)
        {
            ProcessedBy = instanceId;
            LockedUntil = DateTime.UtcNow.AddSeconds(lockTimeoutSeconds);
            return true;
        }
        return false;
    }

    public void ReleaseLock()
    {
        ProcessedBy = null;
        LockedUntil = null;
    }

    public void IncrementRetryCount()
    {
        RetryCount++;
    }

    /// <summary>
    /// Defers re-pickup of this message by <paramref name="delay"/>.
    /// The outbox query skips messages where LockedUntil > UtcNow,
    /// so this effectively schedules a retry without blocking any thread.
    /// </summary>
    public void ScheduleRetry(TimeSpan delay)
    {
        LockedUntil = DateTime.UtcNow.Add(delay);
    }

    private OutboxMessage() { }
}

