using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.Configurations;

public class StoredCardConfiguration : IEntityTypeConfiguration<StoredCard>
{
    public void Configure(EntityTypeBuilder<StoredCard> builder)
    {
        builder.ToTable("StoredCards");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId)
            .IsRequired();

        builder.Property(t => t.OrderId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.CardHolderName)
            .HasMaxLength(255);

        builder.Property(t => t.CardNo)
            .HasMaxLength(255);

        builder.Property(t => t.CardExpiry)
            .HasMaxLength(10);

        builder.Property(t => t.CustomerToken)
            .HasMaxLength(500);

        builder.Property(t => t.CardType)
            .HasMaxLength(100);

        builder.Property(t => t.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt);
    }
}
