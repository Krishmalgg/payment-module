using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace PaymentModule.Infrastructure.Communication.Core.Connection;

/// <summary>
/// Creates the RabbitMQ queue topology required by the payment server before consumers start.
/// </summary>
public sealed class RabbitMqTopologyInitializer
{
    private static readonly string[] Features = ["initiate", "addcard", "storedcards", "refund"];
    private static readonly (string Name, int TtlMs)[] RetryStages = [("2s", 2000), ("15s", 15000)];
    private static readonly string[] NotificationQueues =
    [
        "main.notifications.status.requests",
        "main.notifications.suspicious.requests"
    ];

    private readonly RabbitMqConnection _connection;
    private readonly ILogger<RabbitMqTopologyInitializer> _logger;

    public RabbitMqTopologyInitializer(
        RabbitMqConnection connection,
        ILogger<RabbitMqTopologyInitializer> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[RabbitMQ] Initializing queue topology...");

        var connection = await _connection.GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: "payment.dlx",
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        _logger.LogInformation("[RabbitMQ] ✓ Exchange: payment.dlx");

        foreach (var feature in Features)
        {
            var prefix = $"payment.{feature}";
            var requestQueue = $"{prefix}.requests";
            var dlqQueue = $"{prefix}.requests.dlq";
            var responseQueue = $"{prefix}.responses";

            await DeclareRetryPipelineAsync(channel, requestQueue, cancellationToken);

            await DeclareQueueReplacingIfNeededAsync(channel, responseQueue, arguments: null, cancellationToken);
            _logger.LogInformation("[RabbitMQ] ✓ Response queue: {Queue}", responseQueue);
        }

        foreach (var notificationQueue in NotificationQueues)
        {
            await DeclareRetryPipelineAsync(channel, notificationQueue, cancellationToken);
        }

        _logger.LogInformation("[RabbitMQ] ✅ All queues initialized. Backend can now connect.");
    }

    private async Task DeclareRetryPipelineAsync(
        IChannel channel,
        string mainQueue,
        CancellationToken cancellationToken)
    {
        var dlqQueue = $"{mainQueue}.dlq";

        await DeclareQueueReplacingIfNeededAsync(channel, mainQueue, arguments: null, cancellationToken);
        _logger.LogInformation("[RabbitMQ] ✓ Request queue: {Queue}", mainQueue);

        foreach (var (stageName, ttlMs) in RetryStages)
        {
            var retryQueue = $"{mainQueue}.retry.{stageName}";
            var retryArguments = new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = mainQueue,
                ["x-message-ttl"] = ttlMs
            };

            await DeclareQueueReplacingIfNeededAsync(channel, retryQueue, retryArguments, cancellationToken);
        }

        _logger.LogInformation("[RabbitMQ] ✓ Retry queues: {Queue}.retry.{Stages}", mainQueue, "{2s,15s}");

        await DeclareQueueReplacingIfNeededAsync(channel, dlqQueue, arguments: null, cancellationToken);
        await channel.QueueBindAsync(
            queue: dlqQueue,
            exchange: "payment.dlx",
            routingKey: mainQueue,
            arguments: null,
            cancellationToken: cancellationToken);
        _logger.LogInformation("[RabbitMQ] ✓ DLQ: {Queue}", dlqQueue);
    }

    private async Task DeclareQueueReplacingIfNeededAsync(
        IChannel channel,
        string queueName,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: arguments,
                cancellationToken: cancellationToken);
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
        {
            _logger.LogWarning(
                "[RabbitMQ] Queue '{Queue}' exists with incompatible arguments. Recreating it.",
                queueName);

            await channel.QueueDeleteAsync(queue: queueName, ifUnused: false, ifEmpty: false, cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: arguments,
                cancellationToken: cancellationToken);
        }
    }
}