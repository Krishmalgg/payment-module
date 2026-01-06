using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        // Table mapping
        builder.ToTable("transactions");
        
        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .IsRequired();
        
        // Business Identity
        builder.Property(x => x.OrderId)
            .HasColumnName("order_id")
            .HasMaxLength(50)
            .IsRequired();
        
        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();
        
        // Payment Details
        builder.Property(x => x.Amount)
            .HasColumnName("amount")
            .HasColumnType("decimal(10,2)")
            .IsRequired();
        
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .HasDefaultValue("LKR")
            .IsRequired();
        
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("PENDING")
            .IsRequired();
        
        // Provider Information
        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(20)
            .IsRequired();
        
        builder.Property(x => x.ProviderRefId)
            .HasColumnName("provider_ref_id")
            .HasMaxLength(100);
        
        // Customer Snapshot
        builder.Property(x => x.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(255)
            .IsRequired();
        
        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(255);
        
        // Timestamps
        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");
        
        // BaseEntity audit fields (snake_case mapping)
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");
        
        // Indexes for performance
        builder.HasIndex(x => x.OrderId)
            .HasDatabaseName("idx_transactions_order_id");
        
        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("idx_transactions_user_id");
    }
}
