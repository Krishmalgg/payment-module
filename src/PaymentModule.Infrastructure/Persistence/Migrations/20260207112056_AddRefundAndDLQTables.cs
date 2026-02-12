using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentModule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundAndDLQTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_transactions_order_id",
                table: "transactions");

            migrationBuilder.AddColumn<string>(
                name: "transaction_ref",
                table: "transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dead_letter_queue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letter_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    refund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_refund_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.refund_id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_transactions_order_id",
                table: "transactions",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_transactions_transaction_ref",
                table: "transactions",
                column: "transaction_ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dlq_refund_id",
                table: "dead_letter_queue",
                column: "refund_id");

            migrationBuilder.CreateIndex(
                name: "idx_refunds_transaction_ref",
                table: "refunds",
                column: "transaction_ref",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letter_queue");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropIndex(
                name: "idx_transactions_order_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "idx_transactions_transaction_ref",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "transaction_ref",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "idx_transactions_order_id",
                table: "transactions",
                column: "order_id");
        }
    }
}
