namespace PaymentModule.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

public class WalletConfig : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.TenantId).IsRequired();

        builder.OwnsOne(x => x.Balance)
               .Property(m => m.Value)
               .HasColumnName("balance_value")
               .HasColumnType("numeric(18,2)")
               .IsRequired();

        builder.OwnsOne(x => x.Balance)
               .Property(m => m.Currency)
               .HasColumnName("balance_currency")
               .HasMaxLength(3)
               .IsRequired();

        builder.HasIndex(x => x.TenantId);
    }
}
