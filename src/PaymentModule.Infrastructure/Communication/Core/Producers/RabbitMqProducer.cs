using System.Text;
using RabbitMQ.Client;
using Microsoft.Extensions.Logging;
using PaymentModule.Infrastructure.Communication.Core.Connection;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;

namespace PaymentModule.Infrastructure.Communication.Core.Producers;

/// <summary>
/// Generic Producer for sending messages to RabbitMQ with Publisher Confirms.
/// Uses RabbitMqChannelPool — borrows a pre-warmed channel, publishes, returns it.
/// Cost: 1 network round-trip per message (down from 4-5 with per-message channel create/destroy).
/// </summary>
public class RabbitMqProducer : IMessageProducer
{
    private readonly RabbitMqChannelPool _channelPool;
    private readonly ILogger<RabbitMqProducer> _logger;

    public RabbitMqProducer(RabbitMqChannelPool channelPool, ILogger<RabbitMqProducer> logger)
    {
        _channelPool = channelPool;
        _logger = logger;
    }

    public async Task<ProducerResult> SendAsync(string queue, string payload, CancellationToken ct, string? correlationId = null)
    {
        var channel = await _channelPool.BorrowAsync(ct);
        try
        {
            // Declare queue once per queue name for the pool lifetime — no-op on subsequent calls
            await _channelPool.EnsureQueueDeclaredAsync(channel, queue, ct);

            var body = Encoding.UTF8.GetBytes(payload);
            var properties = new BasicProperties
            {
                Persistent = true // Message survives broker restart
            };

            if (!string.IsNullOrEmpty(correlationId))
                properties.CorrelationId = correlationId;

            _logger.LogInformation("Publishing to RabbitMQ Queue: {Queue}", queue);

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: queue,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            _logger.LogInformation("Message published and confirmed.");
            return ProducerResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to RabbitMQ.");
            return ProducerResult.Fail(ex.Message);
        }
        finally
        {
            // Always return the channel — ReturnAsync handles broken channels gracefully
            await _channelPool.ReturnAsync(channel);
        }
    }
}

