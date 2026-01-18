using System.Text;
using RabbitMQ.Client;
using Microsoft.Extensions.Logging;
using PaymentModule.Infrastructure.Communication.Core.Connection;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;

namespace PaymentModule.Infrastructure.Communication.Core.Producers;

/// <summary>
/// Generic Producer for sending messages to RabbitMQ with Publisher Confirms.
/// </summary>
public class RabbitMqProducer : IMessageProducer
{
    private readonly RabbitMqConnection _connectionPool;
    private readonly ILogger<RabbitMqProducer> _logger;

    public RabbitMqProducer(RabbitMqConnection connectionPool, ILogger<RabbitMqProducer> logger)
    {
        _connectionPool = connectionPool;
        _logger = logger;
    }

    public async Task<ProducerResult> SendAsync(string queue, string payload, CancellationToken ct)
    {
        try
        {
            // 1. Get Connection (Singleton)
            var connection = await _connectionPool.GetConnectionAsync(ct);

            // 2. Create Channel (Lightweight)
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            );
            
            using var channel = await connection.CreateChannelAsync(channelOptions, ct);

            // 3. Declare Queue (Durable = survive restart)
            await channel.QueueDeclareAsync(
                queue: queue, 
                durable: true, 
                exclusive: false, 
                autoDelete: false, 
                arguments: null,
                cancellationToken: ct);

            // 4. Prepare Message
            var body = Encoding.UTF8.GetBytes(payload);
            var properties = new BasicProperties
            {
                Persistent = true // Message survives restart
            };

            // 5. Publish
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
    }
}
