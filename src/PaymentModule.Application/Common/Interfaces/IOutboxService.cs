namespace PaymentModule.Application.Common.Interfaces;

public interface IOutboxService
{
    Task AddMessageAsync(string type, string payload, CancellationToken cancellationToken = default);
    Task<IEnumerable<Guid>> GetUnprocessedMessageIdsAsync(int batchSize = 10, int maxRetryAttempts = 3, CancellationToken cancellationToken = default);
    Task<Domain.Entities.OutboxMessage?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task ProcessMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task ProcessMessageAsync(Guid messageId, string instanceId, CancellationToken cancellationToken = default);
    Task IncrementRetryCountAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Defers re-pickup of a failed message by <paramref name="delay"/> without blocking
    /// the processing thread. Other messages continue processing immediately.
    /// </summary>
    Task ScheduleRetryAsync(Guid messageId, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message as permanently failed: increments RetryCount past the max
    /// (so it is never picked up again) and writes a FailedMessages entry.
    /// </summary>
    Task MoveToDlqAsync(Guid messageId, string reason, string communicationType, CancellationToken cancellationToken = default);

    // Lock management for Level 3 distributed processing
    Task<bool> TryAcquireLockAsync(Guid messageId, string instanceId, int lockTimeoutSeconds, CancellationToken cancellationToken = default);
    Task ReleaseLockAsync(Guid messageId, CancellationToken cancellationToken = default);
}


