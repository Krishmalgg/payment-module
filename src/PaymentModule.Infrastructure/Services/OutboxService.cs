using Microsoft.EntityFrameworkCore;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Entities;
using PaymentModule.Infrastructure.Persistence.DbContext;

namespace PaymentModule.Infrastructure.Services;

public class OutboxService : IOutboxService
{
    private readonly SecureDbContext _dbContext;

    public OutboxService(SecureDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddMessageAsync(string type, string payload, CancellationToken cancellationToken = default)
    {
        var message = new OutboxMessage(type, payload);
        _dbContext.OutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<Guid>> GetUnprocessedMessageIdsAsync(int batchSize = 10, int maxRetryAttempts = 3, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null
                     && m.RetryCount < maxRetryAttempts
                     && (m.LockedUntil == null || m.LockedUntil <= now))
            .OrderBy(m => m.OccurredAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<OutboxMessage?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
    }

    public async Task ProcessMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message != null)
        {
            message.MarkAsProcessed("unknown");
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ProcessMessageAsync(Guid messageId, string instanceId, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message != null)
        {
            message.MarkAsProcessed(instanceId);
            message.ReleaseLock();  // Clear the lock after processing
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task IncrementRetryCountAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message != null)
        {
            message.IncrementRetryCount();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ScheduleRetryAsync(Guid messageId, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message != null)
        {
            message.IncrementRetryCount();
            message.ScheduleRetry(delay);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MoveToDlqAsync(Guid messageId, string reason, string communicationType, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message == null) return;

        // Increment past max so GetUnprocessedMessageIdsAsync never returns it again
        message.IncrementRetryCount();

        var entry = new FailedMessage(
            id: Guid.NewGuid(),
            messageType: message.Type,
            source: "outbox",
            communicationType: communicationType,
            payload: message.Payload,
            failureReason: reason,
            retryCount: message.RetryCount);

        _dbContext.FailedMessages.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Attempts to acquire a distributed lock for a message.
    /// Returns true if lock was acquired, false if another instance holds the lock.
    /// </summary>
    public async Task<bool> TryAcquireLockAsync(Guid messageId, string instanceId, int lockTimeoutSeconds, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message == null)
            return false;

        // Check if lock is still held by another instance
        if (message.LockedUntil != null && message.LockedUntil > DateTime.UtcNow && message.ProcessedBy != instanceId)
        {
            return false;
        }

        // Acquire or refresh the lock
        message.AcquireLock(instanceId, lockTimeoutSeconds);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lock was acquired by another instance in the meantime
            return false;
        }
    }

    /// <summary>
    /// Releases the distributed lock for a message.
    /// </summary>
    public async Task ReleaseLockAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message != null)
        {
            message.ReleaseLock();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
