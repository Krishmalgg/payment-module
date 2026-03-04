using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentModule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FailedMessages_ReplaceDeadLetterQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop old indexes before rename
            migrationBuilder.DropIndex(name: "idx_dlq_refund_id",    table: "DeadLetterQueue");
            migrationBuilder.DropIndex(name: "idx_dlq_message_type", table: "DeadLetterQueue");
            migrationBuilder.DropIndex(name: "idx_dlq_resolved",     table: "DeadLetterQueue");

            // Drop legacy RefundId column (no longer part of FailedMessage)
            migrationBuilder.DropColumn(name: "RefundId", table: "DeadLetterQueue");

            // Rename SourceQueue → Source
            migrationBuilder.RenameColumn(
                name: "SourceQueue",
                table: "DeadLetterQueue",
                newName: "Source");

            // Rename the table
            migrationBuilder.RenameTable(
                name: "DeadLetterQueue",
                newName: "FailedMessages");

            // Rename the primary key constraint
            migrationBuilder.DropPrimaryKey(name: "PK_DeadLetterQueue", table: "FailedMessages");
            migrationBuilder.AddPrimaryKey(name: "PK_FailedMessages",   table: "FailedMessages", column: "Id");

            // Add new CommunicationType column (backfill with "RabbitMq" for existing rows)
            migrationBuilder.AddColumn<string>(
                name: "CommunicationType",
                table: "FailedMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "RabbitMq");

            // Recreate indexes with new names
            migrationBuilder.CreateIndex(
                name: "idx_failedmsg_comm_type",
                table: "FailedMessages",
                column: "CommunicationType");

            migrationBuilder.CreateIndex(
                name: "idx_failedmsg_message_type",
                table: "FailedMessages",
                column: "MessageType");

            migrationBuilder.CreateIndex(
                name: "idx_failedmsg_resolved",
                table: "FailedMessages",
                column: "Resolved");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "idx_failedmsg_comm_type",    table: "FailedMessages");
            migrationBuilder.DropIndex(name: "idx_failedmsg_message_type", table: "FailedMessages");
            migrationBuilder.DropIndex(name: "idx_failedmsg_resolved",     table: "FailedMessages");

            migrationBuilder.DropColumn(name: "CommunicationType", table: "FailedMessages");

            migrationBuilder.DropPrimaryKey(name: "PK_FailedMessages", table: "FailedMessages");
            migrationBuilder.AddPrimaryKey(name: "PK_DeadLetterQueue", table: "FailedMessages", column: "Id");

            migrationBuilder.RenameTable(name: "FailedMessages", newName: "DeadLetterQueue");

            migrationBuilder.RenameColumn(
                name: "Source",
                table: "DeadLetterQueue",
                newName: "SourceQueue");

            migrationBuilder.AddColumn<Guid>(
                name: "RefundId",
                table: "DeadLetterQueue",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(name: "idx_dlq_message_type", table: "DeadLetterQueue", column: "MessageType");
            migrationBuilder.CreateIndex(name: "idx_dlq_refund_id",    table: "DeadLetterQueue", column: "RefundId");
            migrationBuilder.CreateIndex(name: "idx_dlq_resolved",     table: "DeadLetterQueue", column: "Resolved");
        }
    }
}
