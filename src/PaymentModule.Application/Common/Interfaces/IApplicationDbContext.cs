using Microsoft.EntityFrameworkCore;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
