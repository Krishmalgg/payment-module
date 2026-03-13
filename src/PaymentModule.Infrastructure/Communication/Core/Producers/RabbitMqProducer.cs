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

    public async Task<ProducerResult> SendAsync(
        string queue,
        string payload,
        CancellationToken ct,
        string? correlationId = null,
        IDictionary<string, object?>? headers = null)
    {
        var channel = await _channelPool.BorrowAsync(ct);
        try
        {
            // Declare queue once per queue name for the pool lifetime — no-op on subsequent calls
            await _channelPool.EnsureQueueDeclaredAsync(channel, queue, ct);

            var body = Encoding.UTF8.GetBytes(payload);
            var timestamp = ExtractTimestamp(headers);
            var properties = new BasicProperties
            {
                Persistent = true, // Message survives broker restart
                Timestamp = new AmqpTimestamp(timestamp)
            };

            if (!string.IsNullOrEmpty(correlationId))
                properties.CorrelationId = correlationId;

            var sanitizedHeaders = SanitizeHeaders(headers);
            if (sanitizedHeaders.Count > 0)
                properties.Headers = sanitizedHeaders;

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

    private static long ExtractTimestamp(IDictionary<string, object?>? headers)
    {
        if (headers is not null
            && headers.TryGetValue("x-timestamp", out var timestampValue)
            && long.TryParse(timestampValue?.ToString(), out var parsedTimestamp))
        {
            return parsedTimestamp;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static Dictionary<string, object?> SanitizeHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
            return [];

        var sanitized = new Dictionary<string, object?>();
        foreach (var pair in headers)
        {
            if (string.Equals(pair.Key, "x-timestamp", StringComparison.OrdinalIgnoreCase))
                continue;

            sanitized[pair.Key] = pair.Value;
        }

        return sanitized;
    }
}

