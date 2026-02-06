using Microsoft.EntityFrameworkCore;
using PaymentModule.Domain.Common;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.DbContext;

using PaymentModule.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using PaymentModule.Infrastructure.Persistence.Converters;

public class SecureDbContext : Microsoft.EntityFrameworkCore.DbContext, IApplicationDbContext
{
    private readonly MediatR.IPublisher _publisher;
    private readonly IOutboxTrigger _trigger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IConfiguration _configuration;

    public SecureDbContext(
        DbContextOptions<SecureDbContext> options, 
        MediatR.IPublisher publisher,
        IOutboxTrigger trigger,
        ICurrentUserService currentUserService,
        IConfiguration configuration) : base(options) 
    {
        _publisher = publisher;
        _trigger = trigger;
        _currentUserService = currentUserService;
        _configuration = configuration;
    }

    public Microsoft.EntityFrameworkCore.DbSet<Transaction> Transactions => Set<Transaction>();
    public Microsoft.EntityFrameworkCore.DbSet<StoredCard> StoredCards => Set<StoredCard>();
    public Microsoft.EntityFrameworkCore.DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public Microsoft.EntityFrameworkCore.DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SecureDbContext).Assembly);

        // --- Row-Level Security (Global Query Filter) ---
        // Only allow users to see their own cards
        // IMPORTANT: System processes (webhooks, etc.) must use .IgnoreQueryFilters()
        modelBuilder.Entity<StoredCard>().HasQueryFilter(x => x.UserId == _currentUserService.UserId);

        // --- Column Encryption ---
        var isEncryptionEnabled = _configuration.GetValue<bool>("Security:IsEncryptStoredData");
        
        if (isEncryptionEnabled)
        {
            var key = _configuration.GetValue<string>("Security:EncryptionKey");
            if (!string.IsNullOrEmpty(key))
            {
                var converter = new AesEncryptionConverter(key);

                modelBuilder.Entity<StoredCard>().Property(e => e.CustomerToken).HasConversion(converter);
                modelBuilder.Entity<StoredCard>().Property(e => e.CardHolderName).HasConversion(converter);
                modelBuilder.Entity<StoredCard>().Property(e => e.CardNo).HasConversion(converter);
                modelBuilder.Entity<StoredCard>().Property(e => e.CardType).HasConversion(converter);
            }
        }

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
