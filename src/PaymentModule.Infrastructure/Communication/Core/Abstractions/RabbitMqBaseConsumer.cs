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
///   Attempt 1 — immediately
///   Attempt 2 — immediately (no delay)
///   Attempt 3 — immediately (no delay)
///   After attempt 3 → store in DeadLetterQueue table, Nack without requeue.
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
    private const int MaxRetries = 3;

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
                await ProcessWithRetryAsync(message, props, tag);
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        });
    }

    // ── Retry Loop (non-blocking) ─────────────────────────────────────────────

    private async Task ProcessWithRetryAsync(
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

        // ── Retry loop ────────────────────────────────────────────────────────
        Exception? lastException = null;

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                    _logger.LogInformation(
                        "[{Consumer}] Retry attempt {Attempt}/{Max} on '{Queue}'.",
                        GetType().Name, attempt + 1, MaxRetries, QueueName);

                var success = await ProcessMessageAsync(message, props);
                if (success)
                {
                    await _channel!.BasicAckAsync(deliveryTag, false);
                    if (attempt > 0)
                        _logger.LogInformation("[{Consumer}] Message succeeded on attempt {Attempt}.",
                            GetType().Name, attempt + 1);

                    // Store idempotency record so future duplicates are skipped
                    if (!string.IsNullOrEmpty(idempotencyKey))
                    {
                        using var storeScope = _serviceProvider.CreateScope();
                        var storeValidator = storeScope.ServiceProvider.GetRequiredService<IRequestValidatorService>();
                        await storeValidator.StoreIdempotencyAsync(
                            idempotencyKey,
                            requestHash,
                            response:    "processed",
                            source:      "RabbitMq",
                            requestType: GetType().Name.Replace("Consumer", ""));
                    }

                    return;
                }

                _logger.LogWarning("[{Consumer}] ProcessMessageAsync returned false on attempt {Attempt}/{Max}.",
                    GetType().Name, attempt + 1, MaxRetries);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "[{Consumer}] Exception on attempt {Attempt}/{Max} for queue '{Queue}'.",
                    GetType().Name, attempt + 1, MaxRetries, QueueName);
            }
        }

        // ── All retries exhausted → DLQ ──
        _logger.LogError(lastException,
            "[{Consumer}] All {Max} attempts failed for message on '{Queue}'. Storing in DeadLetterQueue.",
            GetType().Name, MaxRetries, QueueName);

        await StoreToDlqAsync(message, lastException?.Message ?? "ProcessMessageAsync returned false after all retries");
        await _channel!.BasicNackAsync(deliveryTag, false, requeue: false);
    }

    // ── DLQ Persistence ───────────────────────────────────────────────────────

    private async Task StoreToDlqAsync(string payload, string reason)
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
                retryCount: MaxRetries);

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
