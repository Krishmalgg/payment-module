using Microsoft.Extensions.Logging;
using PaymentModule.Domain.Entities;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;

namespace PaymentModule.Infrastructure.Communication.Core.Outbox;

/// <summary>
/// Orchestrates the dispatching of outbox messages to feature-specific handlers.
/// </summary>
public class OutboxDispatcher : IOutboxDispatcher
{
    private readonly IEnumerable<IOutboxMessageHandler> _handlers;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IEnumerable<IOutboxMessageHandler> handlers, ILogger<OutboxDispatcher> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async Task<ProducerResult> DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var handler = _handlers.FirstOrDefault(h => string.Equals(h.MessageType, message.Type, StringComparison.OrdinalIgnoreCase));

        if (handler == null)
        {
            var error = $"No outbox message handler found for type: {message.Type}";
            _logger.LogWarning(error);
            return ProducerResult.Fail(error);
        }

        try
        {
            _logger.LogInformation("Dispatching outbox message {MessageId} to handler {HandlerType}", message.Id, handler.GetType().Name);
            return await handler.HandleAsync(message.Payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while dispatching message {MessageId}", message.Id);
            return ProducerResult.Fail(ex.Message);
        }
    }
}
