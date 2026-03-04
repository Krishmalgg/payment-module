namespace PaymentModule.Application.Common.Interfaces;

/// <summary>
/// Shared retry policy for all outbox processing strategies.
/// Owns the retry schedule (0s → 10s → 60s) and DLQ escalation.
/// Strategies call HandleFailureAsync after a failed dispatch — they do not
/// manage retry delays or DLQ themselves.
/// </summary>
public interface IOutboxRetryPolicy
{
    /// <summary>
    /// Called by any processing strategy after a dispatch failure.
    /// Decides whether to schedule a deferred retry or move the message to the FailedMessages table,
    /// based on how many times the message has already failed (currentRetryCount).
    /// </summary>
    Task HandleFailureAsync(
        Guid messageId,
        int currentRetryCount,
        string? errorReason,
        string communicationType,
        IOutboxService outboxService,
        CancellationToken cancellationToken);
}
