using Microsoft.EntityFrameworkCore;
using PaymentModule.Domain.Common;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.DbContext;

using PaymentModule.Application.Common.Interfaces;

public class SecureDbContext : Microsoft.EntityFrameworkCore.DbContext, IApplicationDbContext
{
    private readonly MediatR.IPublisher _publisher;
    private readonly PaymentModule.Application.Common.Interfaces.IOutboxTrigger _trigger;

    public SecureDbContext(
        Microsoft.EntityFrameworkCore.DbContextOptions<SecureDbContext> options, 
        MediatR.IPublisher publisher,
        PaymentModule.Application.Common.Interfaces.IOutboxTrigger trigger = null!) : base(options) 
    {
        _publisher = publisher;
        _trigger = trigger;
    }

    public Microsoft.EntityFrameworkCore.DbSet<Transaction> Transactions => Set<Transaction>();
    public Microsoft.EntityFrameworkCore.DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public Microsoft.EntityFrameworkCore.DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SecureDbContext).Assembly);

        modelBuilder.Ignore<DomainEvent>();

        // Global query filter removed for Wallet (no longer exists)
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Dispatch Domain Events
        var domainEventEntities = ChangeTracker.Entries<BaseEntity>()
            .Where(x => x.Entity.DomainEvents.Any())
            .Select(x => x.Entity)
            .ToList();

        foreach (var entity in domainEventEntities)
        {
            var events = entity.DomainEvents.ToList();
            entity.ClearDomainEvents();
            
            foreach (var domainEvent in events)
            {
                await _publisher.Publish(domainEvent, cancellationToken);
            }
        }

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
        
        var result = await base.SaveChangesAsync(cancellationToken);
        
        // Signal the outbox worker immediately
        _trigger?.Trigger();
        
        return result;
    }
}
