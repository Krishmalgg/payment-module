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

        // Index for efficient polling
        builder.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
    }
}
