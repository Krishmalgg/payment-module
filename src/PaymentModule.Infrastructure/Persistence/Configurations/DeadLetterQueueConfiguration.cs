using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.Configurations;

public class DeadLetterQueueConfiguration : IEntityTypeConfiguration<DeadLetterQueue>
{
    public void Configure(EntityTypeBuilder<DeadLetterQueue> builder)
    {
        // Table mapping
        builder.ToTable("dead_letter_queue");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .IsRequired();

        // Refund ID
        builder.Property(x => x.RefundId)
            .HasColumnName("refund_id")
            .IsRequired();

        builder.HasIndex(x => x.RefundId)
            .HasDatabaseName("idx_dlq_refund_id");

        // Payload (JSON string)
        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .HasColumnType("text")
            .IsRequired();

        // Failure Reason
        builder.Property(x => x.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(1000)
            .IsRequired();

        // Retry Count
        builder.Property(x => x.RetryCount)
            .HasColumnName("retry_count")
            .IsRequired();

        // Timestamps
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.LastAttemptAt)
            .HasColumnName("last_attempt_at")
            .IsRequired();

        // Resolved
        builder.Property(x => x.Resolved)
            .HasColumnName("resolved")
            .HasDefaultValue(false);
    }
}
