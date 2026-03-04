using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.Configurations;

public class FailedMessageConfiguration : IEntityTypeConfiguration<FailedMessage>
{
    public void Configure(EntityTypeBuilder<FailedMessage> builder)
    {
        builder.ToTable("FailedMessages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MessageType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.CommunicationType)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.FailureReason)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.RetryCount).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.LastAttemptAt).IsRequired();
        builder.Property(x => x.Resolved).HasDefaultValue(false);

        builder.HasIndex(x => x.CommunicationType).HasDatabaseName("idx_failedmsg_comm_type");
        builder.HasIndex(x => x.MessageType).HasDatabaseName("idx_failedmsg_message_type");
        builder.HasIndex(x => x.Resolved).HasDatabaseName("idx_failedmsg_resolved");
    }
}
