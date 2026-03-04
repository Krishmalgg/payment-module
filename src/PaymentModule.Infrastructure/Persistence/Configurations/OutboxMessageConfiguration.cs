using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.Payload)
            .IsRequired();

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.Property(x => x.ProcessedAt)
            .IsRequired(false);

        builder.Property(x => x.ProcessedBy)
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.RetryCount)
            .HasDefaultValue(0);

        builder.Property(x => x.LockedUntil)
            .IsRequired(false);

        // Index for efficient polling - unprocessed messages
        builder.HasIndex(x => new { x.ProcessedAt, x.OccurredAt })
            .HasDatabaseName("IX_OutboxMessages_Unprocessed");

        // Index for lock-aware polling - for Level 3 distributed processing
        builder.HasIndex(x => new { x.ProcessedAt, x.ProcessedBy, x.LockedUntil })
            .HasDatabaseName("IX_OutboxMessages_Locked");

        // Index for retry logic
        builder.HasIndex(x => new { x.ProcessedAt, x.RetryCount })
            .HasDatabaseName("IX_OutboxMessages_Retries");
    }
}
