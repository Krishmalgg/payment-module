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

    public async Task<IEnumerable<Guid>> GetUnprocessedMessageIdsAsync(int batchSize = 10, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
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
            message.MarkAsProcessed();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
