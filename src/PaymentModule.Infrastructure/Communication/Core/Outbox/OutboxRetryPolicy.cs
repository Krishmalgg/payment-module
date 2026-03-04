using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Infrastructure.Configuration;

namespace PaymentModule.Infrastructure.Communication.Core.Outbox;

/// <summary>
/// Implements the shared retry schedule for all outbox processing levels:
///
///   Attempt 1 — immediate (0s)       dispatched by strategy
///   Attempt 2 — after 10s            ScheduleRetryAsync → LockedUntil = now+10s
///   Attempt 3 — after 60s            ScheduleRetryAsync → LockedUntil = now+60s
///   All attempts exhausted → DLQ     MoveToDlqAsync → RetryCount past max + DLQ row
///
/// Delays are driven by OutboxProcessingOptions.RetryDelaysSeconds.
/// Default: [10, 60] → 3 total attempts.
/// </summary>
public class OutboxRetryPolicy : IOutboxRetryPolicy
{
    private readonly TimeSpan[] _retryDelays;
    private readonly ILogger<OutboxRetryPolicy> _logger;

    public OutboxRetryPolicy(
        IOptions<OutboxProcessingOptions> options,
        ILogger<OutboxRetryPolicy> logger)
    {
        _retryDelays = options.Value.RetryDelaysSeconds
            .Select(s => TimeSpan.FromSeconds(s))
            .ToArray();
        _logger = logger;
    }

    public async Task HandleFailureAsync(
        Guid messageId,
        int currentRetryCount,
        string? errorReason,
        string communicationType,
        IOutboxService outboxService,
        CancellationToken cancellationToken)
    {
        if (currentRetryCount < _retryDelays.Length)
        {
            var delay = _retryDelays[currentRetryCount];

            _logger.LogWarning(
                "Outbox: Message {MessageId} failed (attempt {Attempt}/{Total}). " +
                "Scheduling retry in {DelaySeconds}s.",
                messageId,
                currentRetryCount + 1,
                _retryDelays.Length + 1,
                delay.TotalSeconds);

            await outboxService.ScheduleRetryAsync(messageId, delay, cancellationToken);
        }
        else
        {
            _logger.LogError(
                "Outbox: Message {MessageId} exhausted all {Total} attempts [{CommType}]. Moving to FailedMessages. Reason: {Reason}",
                messageId,
                _retryDelays.Length + 1,
                communicationType,
                errorReason ?? "unknown");

            await outboxService.MoveToDlqAsync(
                messageId,
                errorReason ?? "All retry attempts exhausted",
                communicationType,
                cancellationToken);
        }
    }
}
