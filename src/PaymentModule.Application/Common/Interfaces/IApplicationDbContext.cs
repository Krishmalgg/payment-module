using Microsoft.EntityFrameworkCore;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<StoredCard> StoredCards { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<FailedMessage> FailedMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
