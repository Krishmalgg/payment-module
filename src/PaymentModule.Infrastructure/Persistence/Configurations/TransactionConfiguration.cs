using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.WalletId)
            .IsRequired();
        
        builder.OwnsOne(x => x.Amount, money =>
        {
            money.Property(m => m.Value)
                .IsRequired()
                .HasColumnName("Amount");
            
            money.Property(m => m.Currency)
                .IsRequired()
                .HasMaxLength(3)
                .HasColumnName("Currency");
        });
        
        builder.Property(x => x.OccurredAt)
            .IsRequired();
        
        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.ExternalTransactionId)
            .HasMaxLength(255);

        // Audit fields
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.DeletedAt);

        // Index for querying by wallet and date
        builder.HasIndex(x => new { x.WalletId, x.OccurredAt });
    }
}
