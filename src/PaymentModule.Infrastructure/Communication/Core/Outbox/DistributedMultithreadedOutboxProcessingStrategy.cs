using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Infrastructure.Configuration;

namespace PaymentModule.Infrastructure.Communication.Core.Outbox;

/// <summary>
/// Level 3: Distributed multi-threaded processing with distributed locking.
/// Prevents race conditions across multiple running instances.
/// Uses LockedUntil + ProcessedBy columns for per-row lock ownership.
///
/// Retry schedule is owned by IOutboxRetryPolicy (same as Levels 1 &amp; 2):
///   Attempt 1 — immediate (0s)
///   Attempt 2 — after 10s
///   Attempt 3 — after 60s
///   All exhausted → DLQ
///
/// On failure, HandleFailureAsync overwrites LockedUntil with the retry delay,
/// which both releases the distributed lock and schedules the next retry
/// atomically in one DB save — no separate ReleaseLockAsync needed.
/// </summary>
public class DistributedMultithreadedOutboxProcessingStrategy : IOutboxProcessingStrategy
{
    public int ScaleLevel => 3;

    private readonly ILogger<DistributedMultithreadedOutboxProcessingStrategy> _logger;
    private readonly IOutboxRetryPolicy _retryPolicy;
    private readonly int _maxDegreeOfParallelism;
    private readonly int _lockTimeoutSeconds;
    private readonly string _instanceId;
    private readonly string _communicationMode;

    public DistributedMultithreadedOutboxProcessingStrategy(
        ILogger<DistributedMultithreadedOutboxProcessingStrategy> logger,
        IOutboxRetryPolicy retryPolicy,
        IOptions<OutboxProcessingOptions> options)
    {
        _logger = logger;
        _retryPolicy = retryPolicy;
        _maxDegreeOfParallelism = options.Value.MaxDegreeOfParallelism;
        _lockTimeoutSeconds     = options.Value.LockTimeoutSeconds;
        _instanceId             = options.Value.ProcessInstanceId;
        _communicationMode      = options.Value.CommunicationMode;
    }

    public async Task<int> ProcessMessagesAsync(
        IEnumerable<Guid> messageIds,
        IOutboxService outboxService,
        IOutboxDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);

        var tasks = messageIds.Select(async messageId =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                // Acquire distributed lock — prevents another instance from processing same message
                var lockAcquired = await outboxService.TryAcquireLockAsync(
                    messageId, _instanceId, _lockTimeoutSeconds, cancellationToken);

                if (!lockAcquired)
                {
                    _logger.LogDebug(
                        "Outbox (Level 3): Could not acquire lock for {MessageId} — another instance is processing it.",
                        messageId);
                    return 0;
                }

                var message = await outboxService.GetMessageAsync(messageId, cancellationToken);
                if (message == null)
                {
                    await outboxService.ReleaseLockAsync(messageId, cancellationToken);
                    return 0;
                }

                _logger.LogInformation(
                    "Outbox (Level 3): Processing message {MessageId} ({Type}), attempt {Attempt}.",
                    messageId, message.Type, message.RetryCount + 1);

                try
                {
                    var result = await dispatcher.DispatchAsync(message, cancellationToken);

                    if (result.Success)
                    {
                        // ProcessMessageAsync marks processed + clears lock in one save
                        await outboxService.ProcessMessageAsync(messageId, _instanceId, cancellationToken);
                        _logger.LogInformation("Outbox (Level 3): Message {MessageId} processed.", messageId);
                        return 1;
                    }

                    // HandleFailureAsync → ScheduleRetryAsync sets LockedUntil = now+delay,
                    // which overwrites the distributed lock timestamp — effectively releasing
                    // the lock and scheduling the retry atomically.
                    await _retryPolicy.HandleFailureAsync(
                        messageId, message.RetryCount, result.ErrorMessage, _communicationMode, outboxService, cancellationToken);
                    return 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox (Level 3): Exception for message {MessageId}.", messageId);
                    await _retryPolicy.HandleFailureAsync(
                        messageId, message.RetryCount, ex.Message, _communicationMode, outboxService, cancellationToken);
                    return 0;
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }
}
