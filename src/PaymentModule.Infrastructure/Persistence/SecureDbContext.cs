using Microsoft.EntityFrameworkCore;
using PaymentModule.Domain.Common;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.DbContext;

public class SecureDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public SecureDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<SecureDbContext> options) : base(options) { }

    public Microsoft.EntityFrameworkCore.DbSet<Wallet> Wallets => Set<Wallet>();
    public Microsoft.EntityFrameworkCore.DbSet<Transaction> Transactions => Set<Transaction>();
    public Microsoft.EntityFrameworkCore.DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public Microsoft.EntityFrameworkCore.DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SecureDbContext).Assembly);

        // Global query filter for soft delete
        modelBuilder.Entity<Wallet>().HasQueryFilter(w => w.DeletedAt == null);
        modelBuilder.Entity<Transaction>().HasQueryFilter(t => t.DeletedAt == null);

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
