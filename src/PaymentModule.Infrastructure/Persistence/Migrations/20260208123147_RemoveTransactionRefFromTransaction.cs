using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentModule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTransactionRefFromTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_transactions_transaction_ref",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "transaction_ref",
                table: "transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "transaction_ref",
                table: "transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_transactions_transaction_ref",
                table: "transactions",
                column: "transaction_ref",
                unique: true);
        }
    }
}
