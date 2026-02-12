using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.Configurations;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        // Table mapping
        builder.ToTable("refunds");

        // Primary Key
        builder.HasKey(x => x.RefundId);
        builder.Property(x => x.RefundId)
            .HasColumnName("refund_id")
            .IsRequired();

        // Idempotency Guard - TransactionId unique constraint
        builder.Property(x => x.TransactionId)
            .HasColumnName("transaction_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(x => x.TransactionId)
            .HasDatabaseName("idx_refunds_transaction_id")
            .IsUnique();

        // Provider Reference
        builder.Property(x => x.ProviderRefundRef)
            .HasColumnName("provider_refund_ref")
            .HasMaxLength(100);

        // Status
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        // Failure Reason
        builder.Property(x => x.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(500);

        // Amount
        builder.Property(x => x.Amount)
            .HasColumnName("amount")
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        // Currency
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        // Retry Count
        builder.Property(x => x.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0);

        // Timestamps
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");
    }
}
