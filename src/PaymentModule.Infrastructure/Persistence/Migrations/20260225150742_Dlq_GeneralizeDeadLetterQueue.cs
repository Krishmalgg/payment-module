using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentModule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Dlq_GeneralizeDeadLetterQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_dead_letter_queue",
                table: "dead_letter_queue");

            migrationBuilder.RenameTable(
                name: "dead_letter_queue",
                newName: "DeadLetterQueue");

            migrationBuilder.RenameColumn(
                name: "resolved",
                table: "DeadLetterQueue",
                newName: "Resolved");

            migrationBuilder.RenameColumn(
                name: "payload",
                table: "DeadLetterQueue",
                newName: "Payload");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "DeadLetterQueue",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "retry_count",
                table: "DeadLetterQueue",
                newName: "RetryCount");

            migrationBuilder.RenameColumn(
                name: "refund_id",
                table: "DeadLetterQueue",
                newName: "RefundId");

            migrationBuilder.RenameColumn(
                name: "last_attempt_at",
                table: "DeadLetterQueue",
                newName: "LastAttemptAt");

            migrationBuilder.RenameColumn(
                name: "failure_reason",
                table: "DeadLetterQueue",
                newName: "FailureReason");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "DeadLetterQueue",
                newName: "CreatedAt");

            // Postgres requires explicit USING cast when converting text → jsonb
            migrationBuilder.Sql(@"ALTER TABLE ""DeadLetterQueue"" ALTER COLUMN ""Payload"" TYPE jsonb USING ""Payload""::jsonb;");

            migrationBuilder.AlterColumn<Guid>(
                name: "RefundId",
                table: "DeadLetterQueue",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "DeadLetterQueue",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                table: "DeadLetterQueue",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceQueue",
                table: "DeadLetterQueue",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeadLetterQueue",
                table: "DeadLetterQueue",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "idx_dlq_message_type",
                table: "DeadLetterQueue",
                column: "MessageType");

            migrationBuilder.CreateIndex(
                name: "idx_dlq_resolved",
                table: "DeadLetterQueue",
                column: "Resolved");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DeadLetterQueue",
                table: "DeadLetterQueue");

            migrationBuilder.DropIndex(
                name: "idx_dlq_message_type",
                table: "DeadLetterQueue");

            migrationBuilder.DropIndex(
                name: "idx_dlq_resolved",
                table: "DeadLetterQueue");

            migrationBuilder.DropColumn(
                name: "MessageType",
                table: "DeadLetterQueue");

            migrationBuilder.DropColumn(
                name: "SourceQueue",
                table: "DeadLetterQueue");

            migrationBuilder.RenameTable(
                name: "DeadLetterQueue",
                newName: "dead_letter_queue");

            migrationBuilder.RenameColumn(
                name: "Resolved",
                table: "dead_letter_queue",
                newName: "resolved");

            migrationBuilder.RenameColumn(
                name: "Payload",
                table: "dead_letter_queue",
                newName: "payload");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "dead_letter_queue",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RetryCount",
                table: "dead_letter_queue",
                newName: "retry_count");

            migrationBuilder.RenameColumn(
                name: "RefundId",
                table: "dead_letter_queue",
                newName: "refund_id");

            migrationBuilder.RenameColumn(
                name: "LastAttemptAt",
                table: "dead_letter_queue",
                newName: "last_attempt_at");

            migrationBuilder.RenameColumn(
                name: "FailureReason",
                table: "dead_letter_queue",
                newName: "failure_reason");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "dead_letter_queue",
                newName: "created_at");

            migrationBuilder.AlterColumn<string>(
                name: "payload",
                table: "dead_letter_queue",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<Guid>(
                name: "refund_id",
                table: "dead_letter_queue",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "failure_reason",
                table: "dead_letter_queue",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddPrimaryKey(
                name: "PK_dead_letter_queue",
                table: "dead_letter_queue",
                column: "id");
        }
    }
}
