using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace PaymentModule.Infrastructure.Communication.Core.Connection;

/// <summary>
/// Pool of pre-warmed RabbitMQ channels with publisher confirmations enabled.
/// Eliminates the per-message channel create/destroy overhead (was 4-5 round-trips;
/// now 1 round-trip — just the BasicPublishAsync + confirm).
///
/// IChannel is NOT thread-safe, so each channel is used by exactly one caller at a time.
/// SemaphoreSlim acts as a counting gate — limits concurrent borrows to pool size (default 4).
///
/// Queue declarations are tracked globally: each queue name is declared only once
/// across the pool lifetime. QueueDeclareAsync is idempotent but costs a round-trip,
/// so skipping it after first call removes one wasted network frame per message.
/// </summary>
public sealed class RabbitMqChannelPool : IAsyncDisposable
{
    private readonly RabbitMqConnection _connection;
    private readonly ILogger<RabbitMqChannelPool> _logger;
    private readonly int _poolSize;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentQueue<IChannel> _channels = new();
    private readonly ConcurrentDictionary<string, bool> _declaredQueues = new();
    private bool _disposed;

    public RabbitMqChannelPool(
        RabbitMqConnection connection,
        ILogger<RabbitMqChannelPool> logger,
        int poolSize = 4)
    {
        _connection = connection;
        _logger = logger;
        _poolSize = poolSize;
        _semaphore = new SemaphoreSlim(poolSize, poolSize);
    }

    /// <summary>
    /// Borrow a channel from the pool. Always call ReturnAsync in a finally block.
    /// Blocks if all channels are currently in use (respects CancellationToken).
    /// </summary>
    public async Task<IChannel> BorrowAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);

        // Try to get an existing healthy channel from the queue
        while (_channels.TryDequeue(out var existing))
        {
            if (existing.IsOpen)
                return existing;

            // Channel was closed (broker restart etc.) — discard and create a fresh one
            _logger.LogWarning("Pooled channel was closed, discarding and creating a replacement.");
            try { await existing.DisposeAsync(); } catch { /* ignore */ }
        }

        // Pool was empty or all channels were broken — create a new one
        return await CreateChannelAsync(ct);
    }

    /// <summary>
    /// Return a channel to the pool after use.
    /// If the channel is broken it is disposed rather than re-pooled.
    /// Always call this in a finally block to avoid semaphore starvation.
    /// </summary>
    public async Task ReturnAsync(IChannel channel)
    {
        if (!channel.IsOpen)
        {
            _logger.LogWarning("Returned channel was closed; disposing instead of re-pooling.");
            try { await channel.DisposeAsync(); } catch { /* ignore */ }
        }
        else
        {
            _channels.Enqueue(channel);
        }

        _semaphore.Release();
    }

    /// <summary>
    /// Declare a queue if it has not been declared in this pool's lifetime.
    /// After the first declaration the call is a no-op — no network round-trip.
    /// </summary>
    public async Task EnsureQueueDeclaredAsync(IChannel channel, string queue, CancellationToken ct)
    {
        if (_declaredQueues.ContainsKey(queue))
            return;

        await channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        _declaredQueues.TryAdd(queue, true);
        _logger.LogInformation("RabbitMQ queue declared: {Queue}", queue);
    }

    private async Task<IChannel> CreateChannelAsync(CancellationToken ct)
    {
        var connection = await _connection.GetConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            ct);

        _logger.LogDebug("Created new pooled RabbitMQ channel.");
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        while (_channels.TryDequeue(out var ch))
        {
            try { await ch.DisposeAsync(); } catch { /* ignore */ }
        }

        _semaphore.Dispose();
    }
}
