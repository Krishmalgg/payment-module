namespace PaymentModule.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

public class WalletConfig : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");
        
        builder.HasKey(w => w.Id);
        
        builder.Property(w => w.TenantId)
            .IsRequired();
        
        builder.OwnsOne(w => w.Balance, money =>
        {
            money.Property(m => m.Value)
                .IsRequired()
                .HasColumnName("Balance");
            
            money.Property(m => m.Currency)
                .IsRequired()
                .HasMaxLength(3)
                .HasColumnName("Currency");
        });

        // Audit fields
        builder.Property(w => w.CreatedAt).IsRequired();
        builder.Property(w => w.UpdatedAt);
        builder.Property(w => w.DeletedAt);

        // Index for multi-tenancy queries
        builder.HasIndex(w => new { w.TenantId, w.DeletedAt });
    }
}
