using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Infrastructure.Configuration;

namespace PaymentModule.Infrastructure.Communication.Core.Outbox;

/// <summary>
/// Level 1: Sequential processing — processes messages one at a time.
///
/// Retry schedule is entirely owned by IOutboxRetryPolicy:
///   Attempt 1 — immediate (0s)
///   Attempt 2 — after 10s   (scheduled via LockedUntil, no thread blocked)
///   Attempt 3 — after 60s
///   All exhausted → FailedMessages table (CommunicationType from config)
///
/// A single failing message never delays healthy neighbours.
/// </summary>
public class SequentialOutboxProcessingStrategy : IOutboxProcessingStrategy
{
    public int ScaleLevel => 1;

    private readonly ILogger<SequentialOutboxProcessingStrategy> _logger;
    private readonly IOutboxRetryPolicy _retryPolicy;
    private readonly string _communicationMode;

    public SequentialOutboxProcessingStrategy(
        ILogger<SequentialOutboxProcessingStrategy> logger,
        IOutboxRetryPolicy retryPolicy,
        IOptions<OutboxProcessingOptions> options)
    {
        _logger = logger;
        _retryPolicy = retryPolicy;
        _communicationMode = options.Value.CommunicationMode;
    }

    public async Task<int> ProcessMessagesAsync(
        IEnumerable<Guid> messageIds,
        IOutboxService outboxService,
        IOutboxDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var processedCount = 0;

        foreach (var messageId in messageIds)
        {
            var message = await outboxService.GetMessageAsync(messageId, cancellationToken);
            if (message == null) continue;

            _logger.LogInformation(
                "Outbox (Level 1): Processing message {MessageId} ({Type}), attempt {Attempt}.",
                messageId, message.Type, message.RetryCount + 1);

            try
            {
                var result = await dispatcher.DispatchAsync(message, cancellationToken);

                if (result.Success)
                {
                    await outboxService.ProcessMessageAsync(messageId, cancellationToken);
                    _logger.LogInformation("Outbox (Level 1): Message {MessageId} processed.", messageId);
                    processedCount++;
                }
                else
                {
                    await _retryPolicy.HandleFailureAsync(
                        messageId, message.RetryCount, result.ErrorMessage, _communicationMode, outboxService, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox (Level 1): Exception for message {MessageId}.", messageId);
                await _retryPolicy.HandleFailureAsync(
                    messageId, message.RetryCount, ex.Message, _communicationMode, outboxService, cancellationToken);
            }
        }

        return processedCount;
    }
}
