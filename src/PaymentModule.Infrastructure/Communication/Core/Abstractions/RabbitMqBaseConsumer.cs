using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using PaymentModule.Infrastructure.Communication.Core.Connection;
using System.Text;

namespace PaymentModule.Infrastructure.Communication.Core.Abstractions;

/// <summary>
/// Abstract base class for RabbitMQ Consumers.
/// Manages the connection, channel and basic consumer setup.
/// </summary>
public abstract class RabbitMqBaseConsumer : BackgroundService
{
    private readonly RabbitMqConnection _connection;
    private readonly ILogger<RabbitMqBaseConsumer> _logger;
    private IChannel? _channel;
    
    protected abstract string QueueName { get; }

    protected RabbitMqBaseConsumer(RabbitMqConnection connection, ILogger<RabbitMqBaseConsumer> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting {ConsumerName} on queue {QueueName}", GetType().Name, QueueName);

        var retryCount = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var connection = await _connection.GetConnectionAsync(ct);
                _channel = await connection.CreateChannelAsync(cancellationToken: ct);

                // Declare Main Queue (No DLQ arguments)
                await _channel.QueueDeclareAsync(
                    queue: QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: ct);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += OnMessageReceivedAsync;

                await _channel.BasicConsumeAsync(
                    queue: QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: ct);
                
                _logger.LogInformation("{ConsumerName} successfully connected and is consuming from {QueueName}", GetType().Name, QueueName);
                retryCount = 0; // Reset on success

                // Keep the service alive
                while (!ct.IsCancellationRequested && _channel.IsOpen)
                {
                    await Task.Delay(5000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("{ConsumerName} is stopping", GetType().Name);
                break;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
            {
                _logger.LogWarning("Queue {QueueName} has incompatible arguments (likely old DLQ settings). Cleaning up...", QueueName);
                
                try 
                {
                    var connection = await _connection.GetConnectionAsync(ct);
                    using var cleanupChannel = await connection.CreateChannelAsync(cancellationToken: ct);
                    await cleanupChannel.QueueDeleteAsync(QueueName, cancellationToken: ct);
                    _logger.LogInformation("Successfully deleted incompatible queue {QueueName}.", QueueName);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogError(deleteEx, "Failed to delete incompatible queue {QueueName}.", QueueName);
                }

                await Task.Delay(1000, ct); 
            }
            catch (Exception ex)
            {
                retryCount++;
                var delay = Math.Min(30, Math.Pow(2, retryCount)); // Exponential backoff up to 30s
                _logger.LogWarning(ex, "{ConsumerName} failed to connect to RabbitMQ. Retrying in {Delay}s... (Attempt {Count})", GetType().Name, delay, retryCount);
                
                try 
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs @event)
    {
        var body = @event.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);

        try
        {
            _logger.LogInformation("Message received on {QueueName}", QueueName);
            
            var success = await ProcessMessageAsync(message, @event.BasicProperties);

            if (success)
            {
                await _channel!.BasicAckAsync(@event.DeliveryTag, false);
            }
            else
            {
                // Re-queue = true (Original behavior)
                _logger.LogWarning("Processing failed for message on {QueueName}. Re-queueing.", QueueName);
                await _channel!.BasicNackAsync(@event.DeliveryTag, false, true); 
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {QueueName}. Re-queueing.", QueueName);
            await _channel!.BasicNackAsync(@event.DeliveryTag, false, true);
        }
    }

    /// <summary>
    /// Implemented by concrete consumers to process the string message.
    /// </summary>
    protected abstract Task<bool> ProcessMessageAsync(string message, IReadOnlyBasicProperties properties);

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}
