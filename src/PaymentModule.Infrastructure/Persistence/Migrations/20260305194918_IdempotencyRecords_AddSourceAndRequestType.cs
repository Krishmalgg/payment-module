using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentModule.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyRecords_AddSourceAndRequestType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestType",
                table: "IdempotencyRecords",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "IdempotencyRecords",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_RequestType_ExpiresAt",
                table: "IdempotencyRecords",
                columns: new[] { "RequestType", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_RequestType_ExpiresAt",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "IdempotencyRecords");
        }
    }
}
