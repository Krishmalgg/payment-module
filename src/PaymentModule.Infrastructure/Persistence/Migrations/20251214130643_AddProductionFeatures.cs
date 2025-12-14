using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentModule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_wallets",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "IX_wallets_TenantId",
                table: "wallets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_transactions",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_transaction_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Gateway",
                table: "transactions");

            migrationBuilder.RenameTable(
                name: "wallets",
                newName: "Wallets");

            migrationBuilder.RenameTable(
                name: "transactions",
                newName: "Transactions");

            migrationBuilder.RenameColumn(
                name: "balance_value",
                table: "Wallets",
                newName: "Balance");

            migrationBuilder.RenameColumn(
                name: "balance_currency",
                table: "Wallets",
                newName: "Currency");

            migrationBuilder.RenameColumn(
                name: "currency",
                table: "Transactions",
                newName: "Currency");

            migrationBuilder.RenameColumn(
                name: "amount_value",
                table: "Transactions",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "transaction_id",
                table: "Transactions",
                newName: "WalletId");

            migrationBuilder.AlterColumn<decimal>(
                name: "Balance",
                table: "Wallets",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Wallets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Wallets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Wallets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Transactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "Transactions",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalTransactionId",
                table: "Transactions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAt",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Wallets",
                table: "Wallets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Transactions",
                table: "Transactions",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Response = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_TenantId_DeletedAt",
                table: "Wallets",
                columns: new[] { "TenantId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_WalletId_OccurredAt",
                table: "Transactions",
                columns: new[] { "WalletId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                table: "IdempotencyRecords",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_OccurredAt",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Wallets",
                table: "Wallets");

            migrationBuilder.DropIndex(
                name: "IX_Wallets_TenantId_DeletedAt",
                table: "Wallets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Transactions",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_WalletId_OccurredAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ExternalTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Transactions");

            migrationBuilder.RenameTable(
                name: "Wallets",
                newName: "wallets");

            migrationBuilder.RenameTable(
                name: "Transactions",
                newName: "transactions");

            migrationBuilder.RenameColumn(
                name: "Currency",
                table: "wallets",
                newName: "balance_currency");

            migrationBuilder.RenameColumn(
                name: "Balance",
                table: "wallets",
                newName: "balance_value");

            migrationBuilder.RenameColumn(
                name: "Currency",
                table: "transactions",
                newName: "currency");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "transactions",
                newName: "amount_value");

            migrationBuilder.RenameColumn(
                name: "WalletId",
                table: "transactions",
                newName: "transaction_id");

            migrationBuilder.AlterColumn<decimal>(
                name: "balance_value",
                table: "wallets",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "transactions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_value",
                table: "transactions",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<string>(
                name: "Gateway",
                table: "transactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_wallets",
                table: "wallets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_transactions",
                table: "transactions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_wallets_TenantId",
                table: "wallets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_transaction_id",
                table: "transactions",
                column: "transaction_id",
                unique: true);
        }
    }
}
