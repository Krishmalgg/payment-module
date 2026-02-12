using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentModule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameRefundTransactionRefToTransactionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "transaction_ref",
                table: "refunds",
                newName: "transaction_id");

            migrationBuilder.RenameIndex(
                name: "idx_refunds_transaction_ref",
                table: "refunds",
                newName: "idx_refunds_transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "transaction_id",
                table: "refunds",
                newName: "transaction_ref");

            migrationBuilder.RenameIndex(
                name: "idx_refunds_transaction_id",
                table: "refunds",
                newName: "idx_refunds_transaction_ref");
        }
    }
}
