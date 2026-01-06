using Microsoft.EntityFrameworkCore;
using PaymentModule.Domain.Common;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.DbContext;

using PaymentModule.Application.Common.Interfaces;

public class SecureDbContext : Microsoft.EntityFrameworkCore.DbContext, IApplicationDbContext
{
    public SecureDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<SecureDbContext> options) : base(options) { }

    public Microsoft.EntityFrameworkCore.DbSet<Transaction> Transactions => Set<Transaction>();
    public Microsoft.EntityFrameworkCore.DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public Microsoft.EntityFrameworkCore.DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SecureDbContext).Assembly);

        // Global query filter removed for Wallet (no longer exists)
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<BaseEntity>();
        
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(BaseEntity.CreatedAt)).CurrentValue = DateTime.UtcNow;
            }
            
            if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = DateTime.UtcNow;
            }
        }
        
        return await base.SaveChangesAsync(cancellationToken);
    }
}
