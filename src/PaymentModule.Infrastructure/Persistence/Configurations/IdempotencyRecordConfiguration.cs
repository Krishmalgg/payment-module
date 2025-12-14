using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        
        builder.HasKey(x => x.Key);
        
        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(255);
        
        builder.Property(x => x.RequestHash)
            .IsRequired()
            .HasMaxLength(64);
        
        builder.Property(x => x.Response)
            .IsRequired();
        
        builder.Property(x => x.CreatedAt)
            .IsRequired();
        
        builder.Property(x => x.ExpiresAt)
            .IsRequired();

        // Index for cleanup
        builder.HasIndex(x => x.ExpiresAt);
    }
}
