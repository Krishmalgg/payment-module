using Microsoft.EntityFrameworkCore;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
