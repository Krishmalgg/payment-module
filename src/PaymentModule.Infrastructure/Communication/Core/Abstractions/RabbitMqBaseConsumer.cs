using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using PaymentModule.Infrastructure.Communication.Core.Connection;
using PaymentModule.Infrastructure.Persistence.DbContext;
using PaymentModule.Domain.Entities;
using PaymentModule.Application.Common.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace PaymentModule.Infrastructure.Communication.Core.Abstractions;

/// <summary>
/// Abstract base class for all RabbitMQ consumers.
///
/// CONCURRENCY:
///   Up to MaxConcurrency (10) messages are processed simultaneously.
///   OnMessageReceivedAsync returns immediately after dispatching each message
///   to a Task.Run worker — the channel is never blocked waiting for processing.
///
/// RETRY STRATEGY (per message, non-blocking):
///   Attempt 1 — original delivery
///   Attempt 2 — immediate in-process retry
///   Attempt 3 — routed to .retry.2s, then returned by broker to main queue
///   Attempt 4 — routed to .retry.15s, then returned by broker to main queue
///   After attempt 4 → store in FailedMessages and publish to .dlq.
///
/// SECURITY:
///   • Timestamp validation — rejects messages older than 5 s (replay prevention).
///     NOTE: If the Main Server does not set AmqpTimestamp, override SkipTimestampValidation = true
///           in the concrete consumer to disable this check for that queue.
/// </summary>
public abstract class RabbitMqBaseConsumer : BackgroundService
{
    private readonly RabbitMqConnection _connection;
    private readonly ILogger<RabbitMqBaseConsumer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _concurrencySemaphore = new(MaxConcurrency, MaxConcurrency);
    private IChannel? _channel;

    private const int MaxConcurrency = 10;
    private const int FinalFailureAttempt = 4;

    protected abstract string QueueName { get; }

    /// <summary>Override and return true to skip AMQP timestamp validation for this queue.</summary>
    protected virtual bool SkipTimestampValidation => false;

    protected RabbitMqBaseConsumer(
        RabbitMqConnection connection,
        ILogger<RabbitMqBaseConsumer> logger,
        IServiceProvider serviceProvider)
    {
        _connection = connection;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[{Consumer}] Starting on queue '{Queue}'", GetType().Name, QueueName);

        var connectionRetry = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var connection = await _connection.GetConnectionAsync(ct);
                _channel = await connection.CreateChannelAsync(cancellationToken: ct);

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

                _logger.LogInformation("[{Consumer}] Consuming from '{Queue}'", GetType().Name, QueueName);
                connectionRetry = 0;

                while (!ct.IsCancellationRequested && _channel.IsOpen)
                    await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[{Consumer}] Stopping.", GetType().Name);
                break;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
            {
                _logger.LogWarning("[{Consumer}] Queue '{Queue}' has incompatible args — deleting and retrying.", GetType().Name, QueueName);
                await TryDeleteQueueAsync(ct);
                await Task.Delay(1000, ct);
            }
            catch (Exception ex)
            {
                connectionRetry++;
                var delay = Math.Min(30, Math.Pow(2, connectionRetry));
                _logger.LogWarning(ex, "[{Consumer}] RabbitMQ connection failed. Retry in {Delay}s (attempt {Count})",
                    GetType().Name, delay, connectionRetry);
                try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    // ── Message Handler ───────────────────────────────────────────────────────

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs @event)
    {
        // Timestamp check — extract unix time from AMQP properties, validate via shared service
        if (!SkipTimestampValidation)
        {
            var unixTime = @event.BasicProperties.Timestamp.UnixTime;
            if (unixTime <= 0)
            {
                _logger.LogWarning("[{Consumer}] Missing AMQP timestamp — discarding message on '{Queue}'.",
                    GetType().Name, QueueName);
                await _channel!.BasicNackAsync(@event.DeliveryTag, false, requeue: false);
                return;
            }

            using var tsScope = _serviceProvider.CreateScope();
            var tsValidator = tsScope.ServiceProvider.GetRequiredService<IRequestValidatorService>();
            if (!tsValidator.ValidateTimestamp(unixTime))
            {
                _logger.LogWarning("[{Consumer}] Stale AMQP timestamp — discarding message on '{Queue}'.",
                    GetType().Name, QueueName);
                await _channel!.BasicNackAsync(@event.DeliveryTag, false, requeue: false);
                return;
            }
        }

        // Acquire a concurrency slot (blocks only if all 10 workers are busy)
        await _concurrencySemaphore.WaitAsync();

        // Capture locals — @event.Body is a ReadOnlyMemory<byte> tied to the delivery,
        // so copy to array before handing off to the background thread.
        var body    = @event.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);
        var props   = @event.BasicProperties;
        var tag     = @event.DeliveryTag;

        // Fire-and-forget: OnMessageReceivedAsync returns immediately.
        // The channel is free to receive the next delivery right away.
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessWithRetryAsync(body, message, props, tag);
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        });
    }

    // ── Retry Loop (non-blocking) ─────────────────────────────────────────────

    private async Task ProcessWithRetryAsync(
        byte[] body,
        string message,
        IReadOnlyBasicProperties props,
        ulong deliveryTag)
    {
        // ── Idempotency check — skip processing if already handled ────────────
        var idempotencyKey = ExtractIdempotencyKey(props.Headers);
        var requestHash    = ComputeHash(message);

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            using var checkScope = _serviceProvider.CreateScope();
            var validator = checkScope.ServiceProvider.GetRequiredService<IRequestValidatorService>();
            var idempotencyResult = await validator.CheckIdempotencyAsync(idempotencyKey, requestHash);

            if (idempotencyResult.IsDuplicate)
            {
                _logger.LogInformation(
                    "[{Consumer}] Duplicate RabbitMQ message detected — acking without processing. Key={Key}",
                    GetType().Name, idempotencyKey);
                await _channel!.BasicAckAsync(deliveryTag, false);
                return;
            }
        }

        var brokerRetryCount = GetBrokerRetryCount(props.Headers);
        var currentAttempt = brokerRetryCount + 1;

        try
        {
            var success = await ProcessMessageAsync(message, props);
            if (success)
            {
                await CompleteSuccessfulProcessingAsync(deliveryTag, idempotencyKey, requestHash, currentAttempt);
                return;
            }

            _logger.LogWarning(
                "[{Consumer}] Processing returned false on attempt {Attempt} for '{Queue}'.",
                GetType().Name,
                currentAttempt,
                QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[{Consumer}] Exception on attempt {Attempt} for queue '{Queue}'.",
                GetType().Name,
                currentAttempt,
                QueueName);
        }

        if (brokerRetryCount == 0)
        {
            _logger.LogInformation(
                "[{Consumer}] First failure on '{Queue}' — retrying immediately in-process.",
                GetType().Name,
                QueueName);

            try
            {
                var immediateRetrySuccess = await ProcessMessageAsync(message, props);
                if (immediateRetrySuccess)
                {
                    await CompleteSuccessfulProcessingAsync(deliveryTag, idempotencyKey, requestHash, attemptNumber: 2);
                    return;
                }

                _logger.LogWarning(
                    "[{Consumer}] Immediate retry returned false for '{Queue}'. Routing to retry queue.",
                    GetType().Name,
                    QueueName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[{Consumer}] Immediate retry threw for queue '{Queue}'. Routing to retry queue.",
                    GetType().Name,
                    QueueName);
            }
        }

        var targetQueue = brokerRetryCount switch
        {
            0 => $"{QueueName}.retry.2s",
            1 => $"{QueueName}.retry.15s",
            _ => $"{QueueName}.dlq"
        };

        var finalAttemptNumber = brokerRetryCount >= 2 ? FinalFailureAttempt : brokerRetryCount + 2;

        await RouteFailedMessageAsync(body, props, deliveryTag, message, targetQueue, finalAttemptNumber);
    }

    // ── DLQ Persistence ───────────────────────────────────────────────────────

    private async Task CompleteSuccessfulProcessingAsync(
        ulong deliveryTag,
        string? idempotencyKey,
        string requestHash,
        int attemptNumber)
    {
        await _channel!.BasicAckAsync(deliveryTag, false);

        if (attemptNumber > 1)
        {
            _logger.LogInformation(
                "[{Consumer}] Message succeeded on attempt {Attempt} for '{Queue}'.",
                GetType().Name,
                attemptNumber,
                QueueName);
        }

        if (string.IsNullOrEmpty(idempotencyKey))
            return;

        using var storeScope = _serviceProvider.CreateScope();
        var storeValidator = storeScope.ServiceProvider.GetRequiredService<IRequestValidatorService>();
        await storeValidator.StoreIdempotencyAsync(
            idempotencyKey,
            requestHash,
            response:    "processed",
            source:      "RabbitMq",
            requestType: GetType().Name.Replace("Consumer", ""));
    }

    private async Task RouteFailedMessageAsync(
        byte[] body,
        IReadOnlyBasicProperties props,
        ulong deliveryTag,
        string payload,
        string targetQueue,
        int attemptNumber)
    {
        try
        {
            var publishProps = CloneProperties(props);

            await _channel!.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: targetQueue,
                mandatory: false,
                basicProperties: publishProps,
                body: body,
                cancellationToken: CancellationToken.None);

            await _channel.BasicAckAsync(deliveryTag, false);

            if (targetQueue.EndsWith(".dlq", StringComparison.Ordinal))
            {
                _logger.LogError(
                    "[{Consumer}] Attempt {Attempt} failed for '{Queue}'. Message routed to '{TargetQueue}'.",
                    GetType().Name,
                    attemptNumber,
                    QueueName,
                    targetQueue);

                await StoreToDlqAsync(payload, $"Failed after {attemptNumber} attempts", attemptNumber);
            }
            else
            {
                _logger.LogWarning(
                    "[{Consumer}] Attempt {Attempt} failed for '{Queue}'. Message routed to '{TargetQueue}'.",
                    GetType().Name,
                    attemptNumber,
                    QueueName,
                    targetQueue);
            }
        }
        catch (Exception publishEx)
        {
            _logger.LogError(
                publishEx,
                "[{Consumer}] Failed to route message from '{Queue}' to '{TargetQueue}'. Requeueing original delivery.",
                GetType().Name,
                QueueName,
                targetQueue);

            await _channel!.BasicNackAsync(deliveryTag, false, requeue: true);
        }
    }

    private async Task StoreToDlqAsync(string payload, string reason, int retryCount)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureDbContext>();

            var entry = new FailedMessage(
                id: Guid.NewGuid(),
                messageType: GetType().Name.Replace("Consumer", ""),
                source: QueueName,
                communicationType: "RabbitMq",
                payload: payload,
                failureReason: reason,
                retryCount: retryCount);

            db.FailedMessages.Add(entry);
            await db.SaveChangesAsync();

            _logger.LogInformation("[{Consumer}] Poison message stored in FailedMessages (Id={Id}).",
                GetType().Name, entry.Id);
        }
        catch (Exception dbEx)
        {
            _logger.LogCritical(dbEx,
                "[{Consumer}] CRITICAL: failed to persist poison message to FailedMessages! Payload={Payload}",
                GetType().Name, payload);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? ExtractIdempotencyKey(IDictionary<string, object?>? headers)
    {
        if (headers == null) return null;
        if (!headers.TryGetValue("Idempotency-Key", out var value)) return null;
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s     => s,
            _            => value?.ToString()
        };
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(sha256.ComputeHash(bytes));
    }

    private static BasicProperties CloneProperties(IReadOnlyBasicProperties props)
    {
        var clone = new BasicProperties
        {
            AppId = props.AppId,
            ContentEncoding = props.ContentEncoding,
            ContentType = props.ContentType,
            CorrelationId = props.CorrelationId,
            DeliveryMode = props.DeliveryMode,
            Expiration = props.Expiration,
            MessageId = props.MessageId,
            Persistent = props.Persistent,
            Priority = props.Priority,
            ReplyTo = props.ReplyTo,
            Timestamp = props.Timestamp,
            Type = props.Type,
            UserId = props.UserId,
            Headers = props.Headers is null
                ? null
                : new Dictionary<string, object?>(props.Headers)
        };

        return clone;
    }

    private static int GetBrokerRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-death", out var xDeath) || xDeath is not IList<object> deaths)
            return 0;

        var retryCount = 0;

        foreach (var deathEntry in deaths)
        {
            if (deathEntry is not IDictionary<string, object?> death)
                continue;

            var queue = ReadHeaderString(death, "queue");
            if (string.IsNullOrEmpty(queue) || !queue.Contains(".requests.retry.", StringComparison.Ordinal))
                continue;

            retryCount += ReadHeaderCount(death, "count");
        }

        return retryCount;
    }

    private static int ReadHeaderCount(IDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
            return 1;

        return value switch
        {
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            int intValue => intValue,
            uint uintValue => (int)uintValue,
            long longValue => (int)longValue,
            ulong ulongValue => (int)ulongValue,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 1
        };
    }

    private static string? ReadHeaderString(IDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string stringValue => stringValue,
            _ => value.ToString()
        };
    }

    private async Task TryDeleteQueueAsync(CancellationToken ct)
    {
        try
        {
            var conn = await _connection.GetConnectionAsync(ct);
            using var ch = await conn.CreateChannelAsync(cancellationToken: ct);
            await ch.QueueDeleteAsync(QueueName, cancellationToken: ct);
            _logger.LogInformation("[{Consumer}] Deleted incompatible queue '{Queue}'.", GetType().Name, QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Consumer}] Failed to delete queue '{Queue}'.", GetType().Name, QueueName);
        }
    }

    /// <summary>
    /// Implemented by concrete consumers to process one message.
    /// Return true = success (Ack). Return false = failure (triggers retry/DLQ pipeline).
    /// Throw an exception = failure (triggers retry/DLQ pipeline).
    /// </summary>
    protected abstract Task<bool> ProcessMessageAsync(string message, IReadOnlyBasicProperties properties);

    public override void Dispose()
    {
        _channel?.Dispose();
        _concurrencySemaphore.Dispose();
        base.Dispose();
    }
}
