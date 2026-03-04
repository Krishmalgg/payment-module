using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Infrastructure.Configuration;

namespace PaymentModule.Infrastructure.Communication.Core.Outbox;

/// <summary>
/// Level 2: Parallel processing — processes up to MaxDegreeOfParallelism messages concurrently.
///
/// Retry schedule is owned by IOutboxRetryPolicy (same as Level 1):
///   Attempt 1 — immediate (0s)
///   Attempt 2 — after 10s
///   Attempt 3 — after 60s
///   All exhausted → DLQ
///
/// No distributed locking — suitable for single-instance, medium-to-high volume.
/// </summary>
public class ParallelOutboxProcessingStrategy : IOutboxProcessingStrategy
{
    public int ScaleLevel => 2;

    private readonly ILogger<ParallelOutboxProcessingStrategy> _logger;
    private readonly IOutboxRetryPolicy _retryPolicy;
    private readonly int _maxDegreeOfParallelism;
    private readonly string _communicationMode;

    public ParallelOutboxProcessingStrategy(
        ILogger<ParallelOutboxProcessingStrategy> logger,
        IOutboxRetryPolicy retryPolicy,
        IOptions<OutboxProcessingOptions> options)
    {
        _logger = logger;
        _retryPolicy = retryPolicy;
        _maxDegreeOfParallelism = options.Value.MaxDegreeOfParallelism;
        _communicationMode = options.Value.CommunicationMode;
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
                var message = await outboxService.GetMessageAsync(messageId, cancellationToken);
                if (message == null) return 0;

                _logger.LogInformation(
                    "Outbox (Level 2): Processing message {MessageId} ({Type}), attempt {Attempt}.",
                    messageId, message.Type, message.RetryCount + 1);

                try
                {
                    var result = await dispatcher.DispatchAsync(message, cancellationToken);

                    if (result.Success)
                    {
                        await outboxService.ProcessMessageAsync(messageId, cancellationToken);
                        _logger.LogInformation("Outbox (Level 2): Message {MessageId} processed.", messageId);
                        return 1;
                    }

                    await _retryPolicy.HandleFailureAsync(
                        messageId, message.RetryCount, result.ErrorMessage, _communicationMode, outboxService, cancellationToken);
                    return 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox (Level 2): Exception for message {MessageId}.", messageId);
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
